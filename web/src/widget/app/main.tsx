import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './frame.css';
import { listenForHostIdentity, readFrameLanguage, readFrameLocation } from './host';
import { WidgetApp } from './WidgetApp';

const { widgetKey, preview } = readFrameLocation(window.location);
document.documentElement.lang = readFrameLanguage(window.location);
const container = document.getElementById('root');
if (container) {
  const root = createRoot(container);
  if (preview) {
    void import('./preview/PreviewApp').then(({ PreviewApp }) =>
      root.render(
        <StrictMode>
          <PreviewApp />
        </StrictMode>,
      ),
    );
  } else {
    const identity = listenForHostIdentity();
    root.render(
      <StrictMode>
        <WidgetApp widgetKey={widgetKey} identity={identity} />
      </StrictMode>,
    );
  }
}
