// Drives the TinapayanRMS window's page through WebView2's remote debugging port, with playwright-core: the clicks, keys
// and screenshots go to the page, not to the mouse or the screen. Run by driver.ps1, which sets NODE_PATH and SHOTS.
//   node driver.js <port> ready
//   node driver.js <port> shot <name>
//   node driver.js <port> go <address> [name]          a screen's address: products, products/42, settings/keyboard
//   node driver.js <port> click <selector> [name]      a Playwright selector: "text=Products", "button[aria-label='Close']"
//   node driver.js <port> type <text> [name]           into what has the cursor
//   node driver.js <port> press <key> [name]           Enter, Escape, Control+k, ...
//   node driver.js <port> eval <javascript>            prints what it returns, as JSON
//   node driver.js <port> smoke                        every screen and Settings page, then a record of each record
//                                                      screen: fails (exit 1) on the error bar, a screen's error panel,
//                                                      a red toast or an error in the page's console
const { chromium } = require('playwright-core');
const path = require('path');

const [port, command, ...args] = process.argv.slice(2);

(async () => {
  const browser = await chromium.connectOverCDP(`http://localhost:${port}`);
  const page = browser.contexts()[0]?.pages()[0];
  if (!page) {
    throw new Error('The window has no page yet.');
  }

  const shot = async name => {
    if (!name) {
      return;
    }
    // Long enough for a redraw the action asked for, and a load it started, to reach the page.
    await page.waitForTimeout(1200);
    const file = path.join(process.env.SHOTS, `${name}.png`);
    await page.screenshot({ path: file });
    console.log(file);
  };

  switch (command) {
    case 'ready':
      await page.waitForSelector('.app-titlebar', { timeout: 60000 });
      console.log(page.url());
      break;
    case 'shot':
      await shot(args[0] || 'shot');
      break;
    case 'go':
      // Blazor's own navigation, as a link's: a form with unsaved changes asks first.
      await page.evaluate(address => window.Blazor.navigateTo(address), args[0]);
      await shot(args[1]);
      console.log(page.url());
      break;
    case 'click':
      await page.click(args[0], { timeout: 15000 });
      await shot(args[1]);
      break;
    case 'type':
      await page.keyboard.type(args[0]);
      await shot(args[1]);
      break;
    case 'press':
      await page.keyboard.press(args[0]);
      await shot(args[1]);
      break;
    case 'eval':
      console.log(JSON.stringify(await page.evaluate(args[0]), null, 1));
      break;
    case 'smoke':
      process.exitCode = await smoke(page);
      break;
    default:
      throw new Error(`Not a command: ${command}`);
  }

  // Detaches from the page and leaves the window open.
  await browser.close().catch(() => {});
})().catch(error => {
  console.error(String(error).slice(0, 800));
  process.exit(1);
});

// Every screen and Settings page, then the first record in each record screen's list, as a check before an install:
// each visit is waited out and read for what a failure shows. Picking a record writes nothing but the lists' open
// folders in view-state.json, as any pick does. Returns the exit code: 1 when anything failed.
async function smoke(page) {
  const screens = [
    'raw-materials', 'sub-assemblies', 'products', 'reports', 'price-simulator', 'print',
    'settings/units-of-measure', 'settings/material-categories', 'settings/product-categories', 'settings/roles',
    'settings/costing-defaults', 'settings/keyboard', 'settings/database',
  ];
  // The web view asks for a favicon on every move, which the app hasn't ("Failed to load resource:
  // net::ERR_ADDRESS_UNREACHABLE", the address only in the message's location): not a failure.
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error' && !/favicon/i.test(`${message.text()} ${message.location()?.url ?? ''}`)) {
      consoleErrors.push(message.text());
    }
  });
  page.on('pageerror', error => consoleErrors.push(String(error)));

  const seenToasts = new Set();
  let failures = 0;

  // A visit settles once a pick's details are in (no .details-pending), a second and a half at least.
  const settle = async () => {
    await page.waitForTimeout(1500);
    await page.waitForFunction(() => !document.querySelector('.details-pending'), null, { timeout: 15000 }).catch(() => {});
  };

  const check = async step => {
    const state = await page.evaluate(() => ({
      path: location.pathname,
      bar: getComputedStyle(document.getElementById('blazor-error-ui')).display !== 'none',
      panels: document.querySelectorAll('.screen-error').length,
      toasts: [...document.querySelectorAll('.e-toast-danger .e-toast-content')].map(t => t.textContent.trim()),
    }));
    const problems = [];
    if (state.bar) problems.push('the page\'s error bar');
    if (state.panels) problems.push('the screen\'s error panel');
    for (const toast of state.toasts.filter(t => !seenToasts.has(t))) {
      seenToasts.add(toast);
      problems.push(`a red toast: ${toast}`);
    }
    problems.push(...consoleErrors.splice(0).map(error => `a page error: ${error.slice(0, 200)}`));
    console.log(`${problems.length ? 'FAIL' : 'ok  '} ${step.padEnd(34)} ${state.path}${problems.length ? '\n       ' + problems.join('\n       ') : ''}`);
    failures += problems.length ? 1 : 0;
    return state;
  };

  for (const address of screens) {
    await page.evaluate(to => window.Blazor.navigateTo(to), address);
    await settle();
    await check(address);
  }

  for (const screen of ['raw-materials', 'sub-assemblies', 'products']) {
    await page.evaluate(to => window.Blazor.navigateTo(to), screen);
    await settle();
    const name = await page.evaluate(() => {
      // A record's row (a folder's row says whether it's open) that isn't the one picked already: clicking that one
      // changes nothing, so its address wouldn't move.
      const row = [...document.querySelectorAll('.tree-row')]
        .find(r => !r.hasAttribute('aria-expanded') && r.getAttribute('aria-selected') !== 'true');
      if (!row) {
        return null;
      }
      row.click();
      return (row.querySelector('.tree-title') ?? row).textContent.trim();
    });
    await settle();
    const state = await check(`${screen}: ${name ?? '(no record)'}`);
    if (name && !new RegExp(`^/${screen}/\\d+$`).test(state.path)) {
      console.log(`FAIL ${screen}: picking ${name} didn't open it at its own address`);
      failures++;
    }
  }

  console.log(failures ? `${failures} step(s) failed.` : 'Every screen drew without a failure.');
  return failures ? 1 : 0;
}
