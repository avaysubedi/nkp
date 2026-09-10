# NKP judgment search

Public site: **https://avaysubedi.github.io/nkp/**

This is a static GitHub Pages app. The live site loads JSON from `NkpLocal/wwwroot/data/`. After the first visit, search works offline on the phone.

## Update the live site (on your PC)

```powershell
Set-Location NkpLocal
dotnet run --no-launch-profile -- scrape
Set-Location ..
.\update-github-site.ps1
```

`scrape` writes fresh JSON automatically. `update-github-site.ps1` exports JSON (if you skipped scrape) and pushes it. GitHub Pages republishes on every push to `main`.

Local UI only (does not publish): `dotnet run --project NkpLocal` then open http://localhost:5000
