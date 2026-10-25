# Screenshot details / О скриншотах

All seven images are captures of **actual Windows WPF controls from Yarnelo 1.8.0**, rendered on a neutral presentation background. Five show a one-row dock with hover magnification, a compact four-row dock with one-line captions, the English/Russian language menu and settings in both languages. Two comparison boards, in English and Russian, show **Dusk**, **Frost**, **Graphite**, **Ember** and **Aurora** using the production dock controls. Settings include the five-theme picker, **Show icon labels** and **Automatically add new desktop shortcuts**, which is off by default. Long captions are shortened to fit the compact icon cells. The background, comparison labels, headings and footer are presentation artwork, not controls in the app. The Linux interface uses Qt and looks different.

Все семь изображений показывают **настоящие WPF-элементы Windows из Yarnelo 1.8.0**, отрисованные на нейтральном фоне. Пять кадров демонстрируют один ряд с увеличением при наведении, компактные четыре ряда с однострочными подписями, меню языка и настройки на обоих языках. Две сравнительные подборки на русском и английском показывают **Dusk**, **Frost**, **Graphite**, **Ember** и **Aurora**, отрисованные настоящими элементами панели. В настройках видны выбор пяти тем, **«Подписи под значками»** и **«Автоматически добавлять новые ярлыки с рабочего стола»**, по умолчанию выключенная. Длинные подписи сокращаются по ширине компактных ячеек. Фон, названия в сравнительной подборке, заголовки и нижняя подпись добавлены для оформления, они не являются интерфейсом приложения. Linux-версия использует Qt и выглядит иначе.

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
