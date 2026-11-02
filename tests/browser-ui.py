"""Browser regression test against the real loopback server and encrypted vault.
Requires Python playwright plus Chromium. Windows collectors are fixtures.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--chromium', default=None)
parser.add_argument('--output', default='browser-artifacts')
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
checks = 0


def check(condition, message):
    global checks
    assert condition, message
    checks += 1


with tempfile.TemporaryDirectory(prefix='FishTank-browser-ui-') as temp:
    pairing = Path(temp) / 'pairing.txt'
    process = subprocess.Popen([args.dotnet, str(root / 'tests/Browser/bin/Debug/net8.0/Browser.dll'), '--serve-ui', str(pairing)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        for _ in range(150):
            if pairing.exists():
                break
            if process.poll() is not None:
                raise RuntimeError(process.communicate()[1].decode())
            time.sleep(.1)
        check(pairing.exists(), 'Fixture server did not start')
        url = pairing.read_text()
        with sync_playwright() as p:
            launch = {'headless': True}
            if args.chromium:
                launch['executable_path'] = args.chromium
            browser = p.chromium.launch(**launch)
            context = browser.new_context(viewport={'width': 1440, 'height': 1000})
            page = context.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.goto(url)
            expect(page.locator('#workspace')).to_be_visible()
            expect(page.locator('#message')).to_contain_text('Paired')
            check('#pair' not in page.url, 'Pairing token remained in address bar')
            check(page.evaluate('Object.keys(localStorage).length + Object.keys(sessionStorage).length') == 0, 'Persistent browser storage used')
            check(context.cookies() == [], 'Session cookies unexpectedly created')
            page.screenshot(path=str(output / 'overview-desktop.png'), full_page=True)
            page.locator('[data-page="vault"]').click()
            expect(page.locator('#unlock-vault')).to_have_text('Create encrypted vault')
            page.locator('#master').fill('Browser UI fixture master passphrase!')
            page.locator('#confirm-master').fill('Browser UI fixture master passphrase!')
            page.locator('#unlock-vault').click()
            expect(page.locator('#vault-open')).to_be_visible()
            page.locator('#add-email').click()
            page.locator('#entry-title').fill('Personal email')
            page.locator('#entry-email').fill('fixture@example.test')
            page.locator('#entry-password').fill('Email-secret-UI-fixture-123!')
            page.locator('#entry-form button[type="submit"]').click()
            expect(page.locator('#account-list')).to_contain_text('fixture@example.test')
            page.get_by_role('button', name='Connect service', exact=True).click()
            page.locator('#entry-title').fill('EA <script>window.fixtureXss=true</script>')
            page.locator('#entry-username').fill('DistinctEAUsername')
            page.locator('#entry-password').fill('EA-secret-UI-fixture-456!')
            page.locator('#entry-site').fill('ea.com')
            page.locator('#entry-form button[type="submit"]').click()
            expect(page.locator('.connections')).to_contain_text('DistinctEAUsername')
            check(page.evaluate('window.fixtureXss === undefined'), 'Saved title executed JavaScript')
            check('EA-secret-UI' not in page.locator('#account-list').inner_text(), 'List exposed password')
            check('Email-secret-UI' not in page.locator('#account-list').inner_text(), 'List exposed email password')
            page.get_by_role('button', name='Connect service', exact=True).click()
            page.locator('#entry-title').fill('Linked sign-in service')
            page.locator('#linked-signin').check()
            expect(page.locator('#entry-password')).to_be_disabled()
            page.locator('#entry-form button[type="submit"]').click()
            expect(page.locator('.connections')).to_contain_text('Linked sign-in')
            page.locator('#search').fill('DistinctEAUsername')
            expect(page.locator('#account-list')).to_contain_text('fixture@example.test')
            expect(page.locator('#account-list .service')).to_have_count(1)
            page.locator('#search').fill('')
            page.screenshot(path=str(output / 'accounts-desktop.png'), full_page=True)
            for width, height, scale in [(1440, 1000, 1), (1024, 768, 1), (390, 844, 1), (320, 640, 1), (1024, 768, 1.5)]:
                page.set_viewport_size({'width': width, 'height': height})
                # Text enlargement mimics browser accessibility settings without bypassing CSP.
                page.evaluate('(scale) => document.documentElement.style.fontSize = (15 * scale) + "px"', scale)
                if not page.evaluate('document.documentElement.scrollWidth <= innerWidth'):
                    page.screenshot(path=str(output / 'overflow-failure.png'), full_page=True)
                    overflowing = page.evaluate('Array.from(document.querySelectorAll("*")).filter(e => e.getBoundingClientRect().right > innerWidth).map(e => e.tagName + "." + e.className).slice(0,20)')
                    raise AssertionError(f'Page horizontal overflow at {width}/{scale}: {overflowing}')
                check(True, f'Page width {width}/{scale}')
                page.screenshot(path=str(output / f'accounts-{width}-{scale}.png'), full_page=True)
            page.set_viewport_size({'width': 1440, 'height': 1000})
            page.evaluate('document.documentElement.style.fontSize = "15px"')
            page.locator('.service').filter(has_text='DistinctEAUsername').get_by_role('button', name='View / edit').click()
            expect(page.locator('#entry-password')).to_have_value('EA-secret-UI-fixture-456!')
            check(page.locator('#entry-password').get_attribute('type') == 'password', 'Credential was revealed by default')
            page.locator('#reveal-password').click()
            check(page.locator('#entry-password').get_attribute('type') == 'text', 'Explicit reveal failed')
            page.locator('#reveal-password').click()
            for width in (1440, 390, 320):
                page.set_viewport_size({'width': width, 'height': 844})
                check(page.locator('#editor').evaluate('(el) => el.scrollWidth <= el.clientWidth'), f'Editor horizontal overflow at {width}')
                page.screenshot(path=str(output / f'editor-{width}.png'), full_page=True)
            page.locator('#close-editor').click()
            check(page.locator('#entry-password').input_value() == '', 'Closed editor retained secret')
            page.locator('#lock-vault').click()
            expect(page.locator('#vault-locked')).to_be_visible()
            expect(page.locator('#account-list')).to_be_empty()
            check(page.locator('#entry-password').input_value() == '', 'Vault lock retained secret')
            page.locator('#master').fill('Browser UI fixture master passphrase!')
            page.locator('#unlock-vault').click()
            expect(page.locator('#vault-open')).to_be_visible()
            page.locator('#lock-session').click()
            expect(page.locator('#workspace')).to_be_hidden()
            check(page.locator('#entry-password').input_value() == '' and page.locator('#account-list').inner_text() == '', 'Session lock retained account data')
            # Same static origin can load, but a second tab cannot acquire authenticated data.
            another = context.new_page()
            another.goto(page.url)
            expect(another.locator('#workspace')).to_be_hidden()
            check(another.evaluate('async () => (await fetch("/api/status", {method:"POST", headers:{"Content-Type":"application/json"}, body:"{}"})).status') == 401, 'Unpaired tab accessed backend')
            check(not errors, 'Browser JavaScript errors: ' + json.dumps(errors))
            browser.close()
        print(f'{checks} browser UI checks passed; screenshots saved to {output}. Windows collectors used fixtures.')
    finally:
        process.terminate()
        try:
            process.communicate(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate()
