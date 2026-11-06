import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './frame.css';
import { listenForHostIdentity, readFrameLanguage, readFrameLocation } from './host';
import { WidgetApp } from './WidgetApp';

// Served at /widget/v1/frame/{key}, which names the widget, and at /widget/v1/preview for the portal's widget editor.
const { widgetKey, preview } = readFrameLocation(window.location);
document.documentElement.lang = readFrameLanguage(window.location);
const container = document.getElementById('root');
if (container) {
  const root = createRoot(container);
  if (preview) {
    // A chunk of its own, so visitors never download the editor's preview.
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
