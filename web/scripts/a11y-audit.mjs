// axe-core (WCAG 2.2 AA) on every portal route and the widget, in light and dark, against a running stack.
// Usage:
//   npm run a11y                       (the portal on http://localhost:5173 and Keycloak must be running)
//   npm run a11y -- --out=/tmp/wcag --only=/home,/sources --themes=dark
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const BASE = process.env.KA_BASE ?? 'http://localhost:5173';
const USER = process.env.KA_USER ?? 'admin';
const PASSWORD = process.env.KA_PASSWORD ?? 'admin';
const THEME_KEY = 'knowledge-agent.theme';
const AXE = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'];

/** @param {string} name */
const arg = (name) => process.argv.find((value) => value.startsWith(`--${name}=`))?.slice(name.length + 3);
const themes = /** @type {('light' | 'dark')[]} */ (arg('themes')?.split(',') ?? ['light', 'dark']);
const only = arg('only')?.split(',');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const outDir = resolve(arg('out') ?? join(root, 'TestResults', 'a11y'));

/**
 * A state is a route plus an optional step that opens a sheet, a menu or a dialog before axe runs.
 * @typedef {{ name: string, path: string | { from: string, link: string }, open?: (page: import('playwright').Page) => Promise<unknown>, width?: number }} State
 */

/** @param {import('playwright').Page} page @param {string | RegExp} name */
const clickButton = (page, name) => page.getByRole('button', { name }).first().click({ timeout: 5000 });

/** @type {State[]} */
const STATES = [
  { name: '/home', path: '/home' },
  { name: '/inbox', path: '/inbox' },
  { name: '/analytics', path: '/analytics' },
  { name: '/feedback', path: '/feedback' },
  { name: '/playground', path: '/playground' },
  { name: '/agents', path: '/agents' },
  { name: '/agents/new', path: '/agents/new' },
  { name: '/agents/:first', path: { from: '/agents', link: 'a[href^="/agents/"]:not([href="/agents/new"])' } },
  { name: '/widgets', path: '/widgets' },
  { name: '/widgets/new', path: '/widgets/new' },
  { name: '/widgets/:first', path: { from: '/widgets', link: 'a[href^="/widgets/"]:not([href="/widgets/new"])' } },
  { name: '/sources', path: '/sources' },
  { name: '/sources add sheet', path: '/sources', open: (page) => clickButton(page, /^Add/) },
  { name: '/prompts', path: '/prompts' },
  { name: '/prompts/:first', path: { from: '/prompts', link: 'a[href^="/prompts/"]' } },
  { name: '/evals', path: '/evals' },
  { name: '/evals/runs/:first', path: { from: '/evals', link: 'a[href^="/evals/runs/"]' } },
  { name: '/evals/datasets/:first', path: { from: '/evals', link: 'a[href^="/evals/datasets/"]' } },
  { name: '/models', path: '/models' },
  { name: '/notifications', path: '/notifications' },
  { name: '/tools', path: '/tools' },
  { name: '/system', path: '/system' },
  { name: '/team', path: '/team' },
  { name: '/contacts', path: '/contacts' },
  { name: '/contacts/:first', path: { from: '/contacts', link: 'a[href^="/contacts/"]' } },
  { name: '/not-found', path: '/no-such-page' },
  { name: '/home help panel', path: '/home', open: (page) => clickButton(page, /^Help/) },
  { name: '/home account menu', path: '/home', open: (page) => clickButton(page, /^Account/) },
  { name: '/agents actions menu', path: '/agents', open: (page) => clickButton(page, /^Actions for/) },
  { name: '/tools add sheet', path: '/tools', open: (page) => clickButton(page, 'Add MCP server') },
  {
    name: '/widgets/:first preview',
    path: { from: '/widgets', link: 'a[href^="/widgets/"]:not([href="/widgets/new"])' },
    open: (page) => page.goto(`${page.url()}?preview`).then(() => settle(page)),
  },
  { name: '/home 320px', path: '/home', width: 320 },
  { name: '/inbox 320px', path: '/inbox', width: 320 },
  { name: '/sources 320px', path: '/sources', width: 320 },
  { name: '/widgets/:first 320px', path: { from: '/widgets', link: 'a[href^="/widgets/"]:not([href="/widgets/new"])' }, width: 320 },
];

/** @param {import('playwright').Browser} browser */
async function signIn(browser) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/home`);
  await page.waitForLoadState('networkidle').catch(() => undefined);
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
  await page.waitForURL((/** @type {URL} */ url) => url.href.startsWith(BASE), { timeout: 20000 });
  await page.getByRole('navigation', { name: 'Main' }).waitFor({ timeout: 20000 });
  const state = await context.storageState();
  await context.close();
  return state;
}

/** @param {import('playwright').Page} page @param {State['path']} path */
async function resolvePath(page, path) {
  if (typeof path === 'string') {
    return path;
  }
  await page.goto(`${BASE}${path.from}`);
  const link = page.locator(path.link).first();
  const found = await link
    .waitFor({ state: 'visible', timeout: 10000 })
    .then(() => true)
    .catch(() => false);
  return found ? link.getAttribute('href') : null;
}

/** @param {import('playwright').Page | import('playwright').Frame} target */
async function runAxe(target) {
  await target.addScriptTag({ path: AXE });
  return target.evaluate(
    (tags) =>
      // @ts-expect-error axe is the injected global.
      window.axe.run(document, { runOnly: { type: 'tag', values: tags }, resultTypes: ['violations'] }),
    TAGS,
  );
}

/** @param {any} results */
const summarize = (results) =>
  results.violations.map((/** @type {any} */ violation) => ({
    id: violation.id,
    impact: violation.impact,
    help: violation.help,
    nodes: violation.nodes
      .slice(0, 8)
      .map((/** @type {any} */ node) => ({ target: node.target.join(' '), summary: node.failureSummary?.split('\n').slice(0, 3).join(' ') })),
    count: violation.nodes.length,
  }));

/** @param {import('playwright').Page} page */
async function settle(page) {
  await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => undefined);
  await page.waitForTimeout(500);
}

/** The widget frame through the loader, on a host page the audit serves itself. @param {import('playwright').BrowserContext} context @param {string} theme */
async function widgetStates(context, theme) {
  const page = await context.newPage();
  await page.goto(`${BASE}/home`);
  const widgets = await page.evaluate(async () => (await fetch('/api/widgets')).json()).catch(() => []);
  const key = Array.isArray(widgets) ? widgets[0]?.publicKey : undefined;
  /** @type {{ name: string, theme: string, violations: any[], error?: string }[]} */
  const out = [];
  if (!key) {
    out.push({ name: 'widget', theme, violations: [], error: 'no widget' });
    await page.close();
    return out;
  }
  const host = `${BASE}/__a11y-host.html`;
  await page.route(host, (route) =>
    route.fulfill({
      contentType: 'text/html',
      body: `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Host page</title></head><body><main><h1>Host page</h1><p>Shop text.</p></main><script async src="/widget/v1/loader.js" data-widget="${key}"></script></body></html>`,
    }),
  );
  for (const width of [1280, 320]) {
    await page.setViewportSize({ width, height: width === 320 ? 640 : 800 });
    await page.goto(host);
    await settle(page);
    const launcherResults = await runAxe(page);
    out.push({ name: `widget launcher closed ${width}px`, theme, violations: summarize(launcherResults) });
    const launcher = page.locator('[data-knowledge-agent-launcher] button, button[aria-controls], button[aria-expanded]').first();
    await launcher.click({ timeout: 5000 }).catch(() => undefined);
    await page.waitForTimeout(1500);
    const frameHandle = await page
      .locator('iframe')
      .first()
      .elementHandle()
      .catch(() => null);
    const frame = await frameHandle?.contentFrame();
    if (frame) {
      await frame.waitForLoadState('networkidle').catch(() => undefined);
      await page.waitForTimeout(800);
      // The frame is transparent over the loader's panel, which axe cannot see from inside it, so paint the panel color.
      await frame.evaluate(() => {
        const page = /** @type {any} */ (globalThis);
        page.document.body.style.background = page.getComputedStyle(page.document.documentElement).getPropertyValue('--ka-panel');
      });
      out.push({ name: `widget chat open ${width}px`, theme, violations: summarize(await runAxe(frame)) });
      out.push({ name: `widget host with open chat ${width}px`, theme, violations: summarize(await runAxe(page)) });
    } else {
      out.push({ name: `widget chat open ${width}px`, theme, violations: [], error: 'frame did not open' });
    }
  }
  await page.close();
  return out;
}

async function main() {
  await mkdir(outDir, { recursive: true });
  const browser = await chromium.launch();
  const storageState = await signIn(browser);
  /** @type {{ name: string, theme: string, path?: string, violations: any[], error?: string }[]} */
  const results = [];

  for (const theme of themes) {
    // The widget frame's CSP only allows its own scripts, so the injected axe needs bypassCSP.
    const context = await browser.newContext({ storageState, colorScheme: theme, viewport: { width: 1280, height: 800 }, bypassCSP: true });
    await context.addInitScript(([key, value]) => localStorage.setItem(key, value), [THEME_KEY, theme]);
    const page = await context.newPage();
    for (const state of STATES) {
      if (only && !only.includes(state.name)) {
        continue;
      }
      try {
        await page.setViewportSize({ width: state.width ?? 1280, height: 800 });
        const path = await resolvePath(page, state.path);
        if (!path) {
          results.push({ name: state.name, theme, violations: [], error: 'nothing to open' });
          continue;
        }
        await page.goto(`${BASE}${path}`);
        await settle(page);
        if (state.open) {
          await state.open(page);
          await page.waitForTimeout(600);
        }
        const violations = summarize(await runAxe(page));
        results.push({ name: state.name, theme, path, violations });
        console.log(`${theme} ${state.name}: ${violations.map((/** @type {any} */ v) => `${v.id}(${v.impact})`).join(', ') || 'ok'}`);
      } catch (error) {
        results.push({ name: state.name, theme, violations: [], error: String(error).slice(0, 300) });
        console.log(`${theme} ${state.name}: error ${String(error).slice(0, 120)}`);
      }
    }
    if (!only || only.includes('widget')) {
      const widget = await widgetStates(context, theme).catch((error) => [
        { name: 'widget', theme, violations: [], error: String(error).slice(0, 300) },
      ]);
      for (const result of widget) {
        results.push(result);
        console.log(`${theme} ${result.name}: ${result.error ?? (result.violations.map((v) => `${v.id}(${v.impact})`).join(', ') || 'ok')}`);
      }
    }
    await context.close();
  }
  await browser.close();

  const serious = results.flatMap((result) =>
    result.violations
      .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
      .map((violation) => ({ ...violation, state: result.name, theme: result.theme })),
  );
  const lines = [
    `# axe-core on the running app (${new Date().toISOString()})`,
    '',
    `States: ${results.length}. Serious or critical: ${serious.length}.`,
    '',
    ...results.map((result) => {
      const head = `## ${result.theme} ${result.name}${result.path ? ` (${result.path})` : ''}`;
      if (result.error) {
        return `${head}\n\nnot run: ${result.error}\n`;
      }
      if (result.violations.length === 0) {
        return `${head}\n\nno violations\n`;
      }
      return `${head}\n\n${result.violations
        .map(
          (violation) =>
            `- **${violation.id}** (${violation.impact}, ${violation.count}): ${violation.help}\n${violation.nodes.map((/** @type {any} */ node) => `  - \`${node.target}\` ${node.summary ?? ''}`).join('\n')}`,
        )
        .join('\n')}\n`;
    }),
  ];
  await writeFile(join(outDir, 'axe-playwright.json'), JSON.stringify(results, null, 2));
  await writeFile(join(outDir, 'axe-playwright.md'), lines.join('\n'));
  console.log(`\n${results.length} states, ${serious.length} serious or critical, written to ${outDir}`);
  process.exitCode = serious.length > 0 ? 1 : 0;
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
