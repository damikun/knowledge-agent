// Screenshots of every portal route at several widths, in light and dark.
// Usage:
//   npm run shots                      (the portal on http://localhost:5173 and Keycloak must be running)
//   KA_BASE=http://localhost:8080 KA_USER=... KA_PASSWORD=... npm run shots
//   npm run shots -- --only=/home,/sources --widths=1280,1920
import { mkdir, readdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const BASE = process.env.KA_BASE ?? 'http://localhost:5173';
const USER = process.env.KA_USER ?? 'admin';
const PASSWORD = process.env.KA_PASSWORD ?? 'admin';
const THEME_KEY = 'knowledge-agent.theme';

/** @param {string} name */
const arg = (name) => process.argv.find((value) => value.startsWith(`--${name}=`))?.slice(name.length + 3);
/** @type {Record<number, number>} */
const VIEWPORTS = { 680: 900, 1000: 800, 1280: 800, 1440: 900, 1920: 1080, 2560: 1440 };
const widths = (arg('widths')?.split(',').map(Number) ?? Object.keys(VIEWPORTS).map(Number)).filter((width) => VIEWPORTS[width]);
const themes = /** @type {('light' | 'dark')[]} */ (arg('themes')?.split(',') ?? ['light', 'dark']);

// Detail pages are found by following the first card on their list page.
/** @type {(string | { path: string, from: string, link: string })[]} */
const ROUTES = [
  '/home',
  '/inbox',
  '/analytics',
  '/feedback',
  '/playground',
  '/agents',
  { path: '/agents/:first', from: '/agents', link: 'a[href^="/agents/"]:not([href="/agents/new"])' },
  '/widgets',
  { path: '/widgets/:first', from: '/widgets', link: 'a[href^="/widgets/"]:not([href="/widgets/new"])' },
  '/sources',
  '/prompts',
  '/evals',
  '/models',
  '/notifications',
  '/tools',
  '/system',
  '/contacts',
];
const only = arg('only')?.split(',');

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const outDir = join(root, 'notes', 'responsive', new Date().toISOString().slice(0, 10));

/** @param {import('playwright').Browser} browser */
async function signIn(browser) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/home`);
  await page.waitForLoadState('networkidle').catch(() => undefined);
  // The portal's own sign-in screen, when it does not send the browser to Keycloak by itself.
  const signInButton = page.getByRole('button', { name: /sign in/i });
  if (page.url().startsWith(BASE) && (await signInButton.isVisible({ timeout: 3000 }).catch(() => false))) {
    await signInButton.click();
  }
  const keycloak = await page
    .locator('#username')
    .waitFor({ state: 'visible', timeout: 10000 })
    .then(() => true)
    .catch(() => false);
  if (keycloak) {
    await page.fill('#username', USER);
    await page.fill('#password', PASSWORD);
    await page.locator('#kc-login, button[type="submit"], input[type="submit"]').first().click();
  }
  await page.waitForURL((/** @type {URL} */ url) => url.href.startsWith(BASE), { timeout: 20000 }).catch(async () => {
    throw new Error(`Sign-in did not return to the portal: ${(await page.locator('body').innerText()).slice(0, 200)}`);
  });
  await page.getByRole('navigation', { name: 'Main' }).waitFor({ timeout: 20000 }).catch(async () => {
    throw new Error(`The portal did not load after sign-in at ${page.url()}: ${(await page.locator('body').innerText()).slice(0, 300)}`);
  });
  const state = await context.storageState();
  await context.close();
  return state;
}

/**
 * @param {import('playwright').Page} page
 * @param {(typeof ROUTES)[number]} route
 */
async function resolvePath(page, route) {
  if (typeof route === 'string') {
    return route;
  }
  await page.goto(`${BASE}${route.from}`);
  const link = page.locator(route.link).first();
  // isVisible does not wait, so wait for the list to render first.
  const found = await link
    .waitFor({ state: 'visible', timeout: 10000 })
    .then(() => true)
    .catch(() => false);
  if (!found) {
    return null;
  }
  return link.getAttribute('href');
}

/** @param {string} path */
const slug = (path) => path.replace(/^\//, '').replace(/[^\w-]+/g, '_') || 'root';

async function main() {
  await mkdir(outDir, { recursive: true });
  const browser = await chromium.launch();
  const storageState = await signIn(browser);
  /** @type {{ route: string, width: number, theme: string, file: string }[]} */
  const shots = [];

  for (const theme of themes) {
    const context = await browser.newContext({ storageState, colorScheme: theme, viewport: { width: 1280, height: 800 } });
    await context.addInitScript(([key, value]) => localStorage.setItem(key, value), [THEME_KEY, theme]);
    const page = await context.newPage();
    for (const route of ROUTES) {
      const name = typeof route === 'string' ? route : route.path;
      if (only && !only.includes(name)) {
        continue;
      }
      const path = await resolvePath(page, route);
      if (!path) {
        console.warn(`skip ${name}: nothing to open`);
        continue;
      }
      for (const width of widths) {
        await page.setViewportSize({ width, height: VIEWPORTS[width] });
        await page.goto(`${BASE}${path}`);
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => undefined);
        // Skeletons and fonts settle.
        await page.waitForTimeout(400);
        const file = `${slug(name)}-${width}-${theme}.png`;
        await page.screenshot({ path: join(outDir, file) });
        shots.push({ route: name, width, theme, file });
        console.log(file);
      }
    }
    await context.close();
  }
  await browser.close();

  // The sheet lists every shot of the day, so a run with --only adds to it instead of replacing it.
  const known = new Map(ROUTES.map((route) => (typeof route === 'string' ? route : route.path)).map((route) => [slug(route), route]));
  for (const file of await readdir(outDir)) {
    const match = /^(.+)-(\d+)-(light|dark)\.png$/.exec(file);
    if (match && !shots.some((shot) => shot.file === file)) {
      shots.push({ route: known.get(match[1]) ?? match[1], width: Number(match[2]), theme: match[3], file });
    }
  }
  shots.sort((a, b) => a.theme.localeCompare(b.theme) || a.width - b.width);
  const order = [...known.values()];
  const rows = [...new Set(shots.map((shot) => shot.route))]
    .sort((a, b) => order.indexOf(a) - order.indexOf(b))
    .map((route) => {
      const cells = shots
        .filter((shot) => shot.route === route)
        .map((shot) => `<figure><a href="${shot.file}"><img src="${shot.file}" loading="lazy" alt=""></a><figcaption>${shot.width} ${shot.theme}</figcaption></figure>`)
        .join('');
      return `<section><h2>${route}</h2><div class="row">${cells}</div></section>`;
    })
    .join('\n');
  const html = `<!doctype html><meta charset="utf-8"><title>Responsive shots</title>
<style>body{font:13px system-ui;margin:16px;background:#f4f4f5;color:#18181b}h2{font-size:14px;margin:24px 0 8px}
.row{display:flex;gap:8px;overflow-x:auto}figure{margin:0;flex:none}img{height:180px;border:1px solid #d4d4d8;border-radius:4px;display:block}
figcaption{color:#52525b;margin-top:4px}</style>
<h1>Responsive shots, ${new Date().toISOString().slice(0, 10)}</h1>
${rows}`;
  await writeFile(join(outDir, 'index.html'), html);
  console.log(`\n${shots.length} shots in ${outDir}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
