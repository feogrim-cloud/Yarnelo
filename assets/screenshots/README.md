# Screenshot details / О скриншотах

These are captures of **actual Windows WPF controls**: the dock and language menu from Yarnelo 1.5, and the two-column settings from Yarnelo 1.7, rendered on a neutral presentation background. They show a one-row dock with hover magnification, a four-row dock with labels, the English/Russian language menu and settings in both languages. The background, headings and footer are presentation artwork, not controls in the app. The Linux interface uses Qt and looks different.

Это изображения **настоящих WPF-элементов Windows**: панель и меню языка Yarnelo 1.5, двухколоночные настройки Yarnelo 1.7 на нейтральном фоне. Показаны один ряд с увеличением при наведении, четыре ряда с подписями, меню языка и настройки на обоих языках. Фон, заголовки и нижняя подпись добавлены для оформления, они не являются интерфейсом приложения. Linux-версия использует Qt и выглядит иначе.

The demo includes Windows system icons, Yarnelo, Steam, Microsoft Edge, Spotify, Control, Warframe, Starfield, Lies of P, Enshrouded, Resident Evil 7, The Ascent, Hail to the Rainbow, Dragon's Dogma 2 and Crimson Desert. Third-party app/game names and icons belong to their respective owners and appear only to illustrate user-added shortcuts. The programs and shortcut collection are not bundled with Yarnelo; no endorsement or affiliation is implied. Yarnelo's all-rights-reserved notice does not extend to third-party icons.

В примере показаны системные значки Windows, Yarnelo и ярлыки перечисленных приложений и игр. Названия и сторонние значки принадлежат своим правообладателям и используются только для демонстрации добавленных пользователем ярлыков. Программы и набор ярлыков не входят в комплект Yarnelo; связь с их разработчиками не подразумевается.

## Reproduce safely

On Windows, create a local UTF-8 JSON manifest containing 12–30 demo entries:

```json
[
  {"Name": "This PC", "Path": "shell:MyComputerFolder"},
  {"Name": "Recycle Bin", "Path": "shell:RecycleBinFolder"},
  {"Name": "Example app", "Path": "C:\\Example\\Example.exe"}
]
```

Use real paths on your own machine. The shortened example above needs additional entries. Then run from the repository root:

```powershell
.\scripts\Render-Screenshots.ps1 -Manifest .\work\demo.json
```

The renderer creates an isolated profile under `work/`, does not show or activate any native dock window, does not start the pictured apps, and does not move or modify the referenced shortcuts. It does not read an installed Yarnelo profile. The language menu and its submenu are rendered directly from the production templates; no popup window is opened. Local manifests, user paths and imported shortcuts are excluded from Git. Only the resulting PNG images and this explanation are published.
