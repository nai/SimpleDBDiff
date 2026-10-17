import { beforeStart, afterStarted } from './_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js';

// WebView hosts do not all run web JS initializers. Fluent's hooks are idempotent,
// so initialize both the custom elements and Blazor events explicitly here.
beforeStart({}, 'webview');
afterStarted(window.Blazor, 'webview');
