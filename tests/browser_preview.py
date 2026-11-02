import asyncio
from playwright.async_api import async_playwright
async def main():
  async with async_playwright() as p:
    browser=await p.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
    page=await browser.new_page(viewport={'width':1440,'height':900},device_scale_factor=1)
    errors=[]
    page.on('pageerror',lambda err:errors.append(str(err)))
    await page.set_content(open('/mnt/data/AimpKaraokeStudio/preview.html').read(),wait_until='load')
    await page.wait_for_timeout(500)
    assert await page.locator('.line').count()==10
    await page.get_by_role('button',name='KARAOKE').click()
    assert await page.locator('#lyrics.karaoke').count()==1
    await page.get_by_role('button',name='READING').click()
    await page.get_by_role('button',name='+',exact=True).click()
    assert await page.locator('#sizeVal').inner_text()=='42'
    await page.screenshot(path='/mnt/data/AimpKaraokeStudio/ui-preview.png',full_page=True)
    print('Browser UI assertions passed; page errors:',errors)
    await browser.close()
asyncio.run(main())
