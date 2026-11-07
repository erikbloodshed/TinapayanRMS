// Drives the TinapayanRMS window's page through WebView2's remote debugging port, with playwright-core: the clicks, keys
// and screenshots go to the page, not to the mouse or the screen. Run by driver.ps1, which sets NODE_PATH and SHOTS.
//   node driver.js <port> ready
//   node driver.js <port> shot <name>
//   node driver.js <port> go <address> [name]          a screen's address: products, products/42, settings/keyboard
//   node driver.js <port> click <selector> [name]      a Playwright selector: "text=Products", "button[aria-label='Close']"
//   node driver.js <port> type <text> [name]           into what has the cursor
//   node driver.js <port> press <key> [name]           Enter, Escape, Control+k, ...
//   node driver.js <port> eval <javascript>            prints what it returns, as JSON
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
    default:
      throw new Error(`Not a command: ${command}`);
  }

  // Detaches from the page and leaves the window open.
  await browser.close().catch(() => {});
})().catch(error => {
  console.error(String(error).slice(0, 800));
  process.exit(1);
});
