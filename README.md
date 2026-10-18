<p align="center">
  <img src="docs/logo.png" width="96" alt="Tarkov Appraiser logo">
</p>

<h1 align="center">Tarkov Appraiser</h1>

<p align="center"><b>Prices for your Escape from Tarkov loot at a glance.</b><br>
Hover an item – Tarkov Appraiser shows what it is worth, right next to the mouse.<br>
Press <b>F8</b>, or switch on the <b>automatic mode</b> and you don't even need a key.</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download the latest version</b></a>
  &nbsp;·&nbsp;
  <a href="#quick-start">Quick start</a>
  &nbsp;·&nbsp;
  <a href="#faq">FAQ</a>
</p>

<!-- In-game clip: record ~5 seconds (hover an item, price box appears) with ScreenToGif and save it as docs/ingame.gif -->
<p align="center">
  <img src="docs/ingame.gif" width="720" alt="Tarkov Appraiser in the game">
</p>

## Two ways to see a price

| | How | Good for |
|---|---|---|
| **Hotkey** | Hover an item, press **F8** | Full control – the box only appears when you ask for it |
| **Automatic mode** | Just rest the mouse on an item for a moment | Sorting your stash or looting fast – no key at all |

The automatic mode is **off by default**. Switch it on in the welcome window or under
*Settings › Detection › Automatic mode*. There you can also set how long the mouse has to rest and keep it
**only in Tarkov** (on by default). **Ctrl+F8** pauses and resumes it at any time – a short notice shows
whether it is on or off.

## What the price box shows

- **Flea market** – lowest price, 24h average and what you get after the fee
- **Traders** – best sell price with the trader's portrait
- **Where to sell** – flea market, trader, or a barter / craft when trading it in brings more, and how much more you get
- **Loot tiers** – prices colored from junk to legendary, valuable loot framed in its tier color; junk is dimmed so you can leave it behind
- **Ammo** – penetration, damage and armor damage; its tier shows how good the round is, while the price keeps the color of what it sells for
- **Armor** – protection class and the real durability of your piece (e.g. 179/190, read from the game)
- **Keys** – which map they are for and how many uses are left (e.g. 37/40, read from the game)
- **Barter & craft tips** – when trading an item in is worth clearly more than selling it
- **Stacks** – the value of exactly what is in the cell (e.g. 18 rounds): read automatically, or typed in with **Shift+F8**
- Price per slot for big items

## Made to fit your way of playing

- **Arrange the price box yourself** – drag & drop in the live preview, hide what you don't need, picture left, right or on top
- Size, opacity, colors and window themes – every change is saved instantly and shown live
- **Compact mode** – just the name and the best way to sell, for fast looting
- **History** – the last 20 items you checked, with current prices – also as a small movable, resizable list in game (Alt+F8)
- **Raid total** – adds up what you checked this raid; click an item in the list for its full price box, right click for tarkov.dev / wiki
- Tray menu with an on/off switch for the automatic mode, and a search field in the settings
- **Statistics** – value checked over all raids, your most valuable finds and most checked items
- An FAQ right in the settings
- PvP and PvE prices, many game languages, English and German user interface
- Works with any screen resolution – Tarkov Appraiser finds the Tarkov window and adapts to it
- Updates with one click, right inside the app

<p align="center">
  <img src="docs/settings.png" width="720" alt="Settings with live preview">
</p>

## Quick start

1. Download **TarkovAppraiser.exe** from the [latest release](../../releases/latest).
2. Start it. The welcome window walks you through the setup,
   then Tarkov Appraiser sits in the tray (bottom right, next to the clock).
3. In Tarkov: hover an item and press **F8** – or turn on the automatic mode and just hover.

Windows may warn about an unknown app the first time: click *More info* › *Run anyway*.

**Requirements:**
- [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) – the .NET 8 SDK or the .NET 8 Desktop Runtime installed
- Windows 10 (version 2004 or newer) or Windows 11
- the Windows text recognition for your game language (*Windows Settings › Time & Language › Language* –
  usually already there for your Windows language; the welcome window checks it for you)

## Hotkeys

| Key | What it does |
|---|---|
| **F8** | Show the price of the item under the mouse |
| **Ctrl+F8** | Pause / resume the automatic mode |
| **Shift+F8** | Type how many are in a stack, then Enter |
| **Alt+F8** | Show / hide the in-game history list |
| **Esc** | Hide the price box |

All hotkeys can be changed under *Settings › General*.

## FAQ

**The price box does not appear.**
Make sure Tarkov runs in *Borderless* or *Windowed* mode and the item's tooltip is visible.
With the automatic mode: check that it is switched on and not paused (Ctrl+F8).

**It shows the wrong item.**
Check *Settings › Detection › Language* – it has to match the game language.
On unusual screens, set the resolution by hand under *Detection › Resolution*.

**Something else is wrong.**
*Settings › About › Report a problem* opens a prepared bug report with the log attached –
or [open an issue](../../issues/new/choose).

## How it works – and fair play

Tarkov Appraiser only looks at your screen: it takes a screenshot around the mouse, reads the item name with
the text recognition built into Windows and looks up the price. It never touches the game: no memory
reading, no injection, no changed game files. The price box itself is hidden from screenshots and
recordings.

Prices and item data come from [tarkov.dev](https://tarkov.dev). Tarkov Appraiser only connects to tarkov.dev
and – for the update check – to GitHub.

Tarkov Appraiser is a fan project and not affiliated with Battlestate Games. Use it at your own risk.

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

- `build.bat` – builds `dist\TarkovAppraiser.exe` (the small version, needs the .NET 8 Desktop Runtime) and starts it; set `SELF_CONTAINED=true` in it for the big version without installation
- `test.bat` – checks the number recognition (stack counts, durability, key uses) against pictures from the game, and the item recognition against recorded tooltips (start the app once first, so the price list is saved)
- New version: see [RELEASING.md](RELEASING.md)
