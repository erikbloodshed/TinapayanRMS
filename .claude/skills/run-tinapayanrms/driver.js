// Drives the TinapayanRMS window's page through WebView2's remote debugging port, with playwright-core: the clicks, keys
// and screenshots go to the page, not to the mouse or the screen. Run by driver.ps1, which sets NODE_PATH and SHOTS.
//   node driver.js <port> ready
//   node driver.js <port> shot <name>
//   node driver.js <port> go <address> [name]          a screen's address: products, products/42, settings/keyboard
//   node driver.js <port> click <selector> [name]      a Playwright selector: "text=Products", "button[aria-label='Close']"
//   node driver.js <port> type <text> [name]           into what has the cursor
//   node driver.js <port> press <key> [name]           Enter, Escape, Control+k, ...
//   node driver.js <port> eval <javascript>            prints what it returns, as JSON
//   node driver.js <port> smoke [--quiet]              every screen and Settings page, then a record of each record
//                                                      screen: fails (exit 1) on the error bar, a screen's error panel,
//                                                      a red toast or an error in the page's console; --quiet prints
//                                                      only what failed
//   node driver.js <port> closecheck                   an edit to a raw material's name, then the window's Close
//                                                      answered Cancel (it stays, the edit kept) and then No (it goes):
//                                                      for a scratch database only (scripts/Test-Window.ps1)
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
      process.exitCode = await smoke(page, args.includes('--quiet'));
      break;
    case 'closecheck':
      // The window goes as No is answered, and the page with it: nothing to detach from.
      process.exit(await closeCheck(page, browser));
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
// folders in view-state.json, as any pick does. Returns the exit code: 1 when anything failed. Quiet, it prints only
// what failed.
async function smoke(page, quiet) {
  const screens = [
    'raw-materials', 'sub-assemblies', 'products', 'reports', 'price-simulator', 'print',
    'settings/units-of-measure', 'settings/material-categories', 'settings/product-categories', 'settings/roles',
    'settings/costing-defaults', 'settings/printing', 'settings/keyboard', 'settings/database',
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

  const settle = () => settled(page);

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
    if (problems.length || !quiet) {
      console.log(`${problems.length ? 'FAIL' : 'ok  '} ${step.padEnd(34)} ${state.path}${problems.length ? '\n       ' + problems.join('\n       ') : ''}`);
    }
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
    // With every folder closed no record shows: one is opened, a few at most, as a person would.
    for (let opened = 0; opened < 3; opened++) {
      const folder = await page.evaluate(() => {
        if ([...document.querySelectorAll('.tree-row')].some(r => !r.hasAttribute('aria-expanded'))) {
          return null;
        }
        // A closed folder with something in it: an empty one's expander draws no icon. The click reaches its row.
        const expander = document.querySelector(".tree-row[aria-expanded='false'] > .tree-expander.e-icons");
        expander?.click();
        return expander ? expander.parentElement.textContent.trim() : null;
      });
      if (!folder) {
        break;
      }
      await page.waitForTimeout(500);
    }
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

  if (failures || !quiet) {
    console.log(failures ? `${failures} step(s) failed.` : 'Every screen drew without a failure.');
  }
  return failures ? 1 : 0;
}

// A visit settles once a pick's details are in (no .details-pending), a second and a half at least. Polled by time, not
// by frame (Playwright's default): a web view off the screen may draw no frames.
async function settled(page) {
  await page.waitForTimeout(1500);
  await page.waitForFunction(() => !document.querySelector('.details-pending'), null, { polling: 100, timeout: 15000 }).catch(() => {});
}

// Waits for what `query` (run in the page) returns to be truthy, polling by time; false when it never is.
function waitInPage(page, query, arg, timeout = 15000) {
  return page.waitForFunction(query, arg, { polling: 100, timeout }).then(() => true, () => false);
}

// The window's close question, both ways, over an edit to a raw material's name: Close answered Cancel keeps the window
// and the edit, and Close answered No lets the window go without saving. Clicks are the page's own (element.click()), as
// a web view off the screen may not take Playwright's. Writes to the form, so only ever on a scratch database: the
// script that runs it (scripts/Test-Window.ps1) checks afterwards that No saved nothing. Returns the exit code.
async function closeCheck(page, browser) {
  const fail = why => {
    console.log(`FAIL the close question: ${why}`);
    return 1;
  };
  // Focus as if the window had it, so the box's own focus and blur events come as a person's would.
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });

  await page.evaluate(() => window.Blazor.navigateTo('raw-materials'));
  await settled(page);
  if (!(await page.evaluate(() => !!document.getElementById('rm-name')))) {
    await page.evaluate(() => [...document.querySelectorAll('.tree-row')].find(r => !r.hasAttribute('aria-expanded'))?.click());
    await settled(page);
  }
  const name = await page.evaluate(() => document.getElementById('rm-name')?.value);
  if (!name) {
    return fail('no raw material opened to edit');
  }
  const edited = `${name} (close check)`;
  await page.evaluate(text => {
    const box = document.getElementById('rm-name');
    box.focus();
    box.value = text;
    box.dispatchEvent(new Event('input', { bubbles: true }));
    box.dispatchEvent(new Event('change', { bubbles: true }));
    box.blur();
  }, edited);
  if (!(await waitInPage(page, () => !!document.querySelector('.record-tab.dirty'), null, 5000))) {
    return fail(`renaming ${name} left no tab with unsaved changes`);
  }
  console.log(`ok   ${name} renamed, unsaved`);

  const close = () => page.evaluate(() => document.querySelector("button[aria-label='Close']").click());
  const asked = () => waitInPage(page, () => [...document.querySelectorAll('.e-dialog.e-popup-open .e-footer-content button')]
    .some(b => b.textContent.trim() === 'Cancel'), null, 10000);
  const answer = text => page.evaluate(label => [...document.querySelectorAll('.e-dialog.e-popup-open .e-footer-content button')]
    .find(b => b.textContent.trim() === label).click(), text);

  await close();
  if (!(await asked())) {
    return fail('Close asked nothing about the unsaved change');
  }
  await answer('Cancel');
  const kept = await waitInPage(page, () => !document.querySelector('.e-dialog.e-popup-open'), null, 5000);
  await page.waitForTimeout(1500);
  const state = await page.evaluate(() => ({
    dirty: !!document.querySelector('.record-tab.dirty'),
    name: document.getElementById('rm-name')?.value,
  }));
  if (!kept || !state.dirty || state.name !== edited) {
    return fail(`Cancel didn't keep the window and the edit (dialog closed: ${kept}, unsaved: ${state.dirty}, name: ${state.name})`);
  }
  console.log('ok   Close answered Cancel: the window stays, the edit kept');

  await close();
  if (!(await asked())) {
    return fail('Close asked nothing the second time');
  }
  // The page goes as the window closes: what the click returns may never come back.
  await Promise.race([answer('No').catch(() => {}), page.waitForTimeout(2000)]);
  console.log('ok   Close answered No');
  await browser.close().catch(() => {});
  return 0;
}
