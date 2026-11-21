// One start of the app's window, seen from its page, for scripts/Compare-Benchmarks.ps1 -Window: started beside the
// app, it waits for WebView2's debugging port, attaches, and marks when the sign-in panel can be typed into (the
// database made ready, DatabaseReadiness) and, once it has signed in with the prepared database's password, when Raw
// Materials' list shows. Each mark is taken in the page as the DOM changes (a MutationObserver), in Unix milliseconds
// (performance.timeOrigin + performance.now()), not from paint: a web view off the screen may not paint at all.
// Prints one line of JSON. playwright-core comes from NODE_PATH, as the run-tinapayanrms driver installs it.
//
// node window-start.js <port> <password> [timeoutMs]
const { chromium } = require('playwright-core');

const [port, password, timeoutText] = process.argv.slice(2);
const timeout = Number(timeoutText || 90000);
const started = Date.now();
const left = () => Math.max(1000, timeout - (Date.now() - started));

async function connect() {
  for (;;) {
    try {
      return await chromium.connectOverCDP(`http://127.0.0.1:${port}`, { timeout: 2000 });
    } catch (error) {
      if (Date.now() - started > timeout) {
        throw new Error(`No web view answered on port ${port} within ${timeout} ms: ${error.message}`);
      }
      await new Promise((resolve) => setTimeout(resolve, 20));
    }
  }
}

// Attached as the web view opens, the page can still be on its first navigation: a query of the page it replaces fails
// ("Execution context was destroyed"), and is asked again of the new one.
async function again(query) {
  for (;;) {
    try {
      return await query();
    } catch (error) {
      if (!/Execution context was destroyed|navigat|Target closed|Cannot find context/i.test(error.message) || Date.now() - started > timeout) {
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
  }
}

// The page's own time when what `name` stands for first holds, at the DOM change that makes it, seen by a
// MutationObserver in the page (playwright-core 1.48's waitForFunction polls by time or by frame, and a page off the
// screen may draw no frames): { at, late }. Late when it held already as the observer went in, so `at` is only when the
// watcher looked, not when the app got there: such a mark is left out (null) rather than timed.
function mark(page, name) {
  const marked = again(() => page.evaluate((what) => new Promise((resolve) => {
    const holds = {
      signIn: () => !!document.querySelector('#sign-in-password'),
      list: () => !!document.querySelector('.tree-row') && !document.querySelector('.details-pending') && !document.querySelector('#sign-in-password'),
    }[what];
    const now = () => performance.timeOrigin + performance.now();
    if (holds()) {
      resolve({ at: now(), late: true });
      return;
    }
    const observer = new MutationObserver(() => {
      if (holds()) {
        observer.disconnect();
        resolve({ at: now(), late: false });
      }
    });
    observer.observe(document, { childList: true, subtree: true, attributes: true });
  }), name));
  const overdue = new Promise((_, reject) => setTimeout(async () => {
    const seen = await describe(page);
    reject(new Error(`${name} wasn't seen within ${timeout} ms: ${seen}`));
  }, left()));
  return Promise.race([marked, overdue]);
}

// What the page shows, for a mark that never came.
async function describe(page) {
  try {
    return JSON.stringify(await page.evaluate(() => ({
      address: location.href,
      signIn: !!document.querySelector('#sign-in-password'),
      said: [...document.querySelectorAll('.sign-in-error, .e-toast-content, [role=alert]')].map((e) => e.textContent.trim()).filter((t) => t),
      rows: document.querySelectorAll('.tree-row').length,
      pending: document.querySelectorAll('.details-pending').length,
      title: document.title,
    })));
  } catch (error) {
    return `the page couldn't be read (${error.message})`;
  }
}

(async () => {
  const browser = await connect();
  const context = browser.contexts()[0];
  const page = context.pages()[0] ?? (await context.waitForEvent('page', { timeout: left() }));
  const attached = await again(() => page.evaluate(() => performance.timeOrigin + performance.now()));

  // The window is off the screen and not the foreground one: focus is emulated, so the panel's own focus lands.
  const cdp = await context.newCDPSession(page);
  await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });

  const signIn = await mark(page, 'signIn');
  const signInReady = signIn.late ? null : signIn.at;

  // The list's mark goes in before any Enter, so it's when the list showed, not when the watcher next looked. It holds
  // only once the sign-in panel has gone, so a refused try doesn't set it off.
  const list = mark(page, 'list');
  list.catch(() => {});

  // Typed once the panel has put the cursor in the box itself, as a person types: typing as soon as the box showed lost
  // keys to the panel's own focus landing partway (the box held only part of the password). At a person's pace, the whole
  // password seen in the box, and a moment for it to reach the panel's state before Enter; a refused try is typed again.
  // None of it is timed: the list's time runs from the Enter that signed in.
  let signingIn = 0;
  let listShown = 0;
  for (let attempt = 1; ; attempt++) {
    const given = await page
      .waitForFunction(() => document.activeElement?.id === 'sign-in-password', null, { polling: 10, timeout: 10000 })
      .then(() => true, () => false);
    if (!given) {
      await page.focus('#sign-in-password');
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
    const focused = await page.evaluate(() => document.activeElement && document.activeElement.id);
    if (focused !== 'sign-in-password') {
      throw new Error(`The password box didn't take the cursor (${focused} has it).`);
    }
    await page.keyboard.type(password, { delay: 20 });
    await page.waitForFunction((typed) => document.querySelector('#sign-in-password')?.value === typed, password, { polling: 20, timeout: 5000 });
    await new Promise((resolve) => setTimeout(resolve, 200));
    signingIn = await page.evaluate(() => performance.timeOrigin + performance.now());
    await page.keyboard.press('Enter');

    // Signed in (the panel gone; the password's check takes well under a second), or the panel says why not.
    const signedIn = await page
      .waitForFunction(() => !document.querySelector('#sign-in-password'), null, { polling: 20, timeout: 8000 })
      .then(() => true, () => false);
    if (signedIn) {
      listShown = (await list).at;
      break;
    }
    if (attempt === 3) {
      throw new Error(`The sign-in panel refused the password three times: ${await describe(page)}`);
    }
  }

  const facts = await page.evaluate(() => ({
    timeOrigin: performance.timeOrigin,
    heap: performance.memory ? performance.memory.usedJSHeapSize : 0,
  }));

  process.stdout.write(JSON.stringify({
    timeOrigin: facts.timeOrigin,
    attached,
    signInReady,
    signingIn,
    listShown,
    heapBytes: facts.heap,
  }) + '\n');
  process.exit(0);
})().catch((error) => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exit(1);
});
