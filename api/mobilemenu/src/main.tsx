import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

// PWA service worker. We register it manually (vite-plugin-pwa injectRegister is
// off) so we can SKIP it inside the admin's <iframe> mock-up preview. A cached
// SW there kept serving a stale index.html that pointed at an old JS hash, so
// after every rebuild the preview 404'd and went blank. Real (top-level)
// customers still get the full offline PWA.
if ('serviceWorker' in navigator) {
  const inIframe = window.top !== window.self;
  const isDev = import.meta.env.DEV;
  if (inIframe || isDev) {
    // Preview/Dev: drop any existing SW so the browser always loads the fresh dev/build.
    navigator.serviceWorker.getRegistrations().then((regs) => regs.forEach((r) => r.unregister()));
  } else {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('./sw.js', { scope: './' }).catch(() => {});
    });
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
