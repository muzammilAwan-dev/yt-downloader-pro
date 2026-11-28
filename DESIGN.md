# YT Downloader Pro: design language (v3)

The rules the Avalonia app follows and the WPF app must follow too (step 4.6c), so both look and feel the same.
Colours are never written into a window: they come from `host/YTDLPHost.Core/Presentation/ThemePalette.cs`
(28 tokens, Dark "Deep ink" and Light "Mist"). Change a colour there and both UIs follow.

## Why these rules (research, 2026-11-19)
- Dark UIs use dark greys (not pure black), desaturated lighter accents, and surfaces that get lighter as they rise
  (Material dark theme, Apple HIG dark mode, Toptal, Muzli surface-hierarchy guide).
- Row actions appear on hover **and** keyboard focus, while the main action stays visible
  (W3C ARIA APG listbox-actions example, Shopify Polaris common actions, Linear's progressive disclosure).
- Control feedback takes 100 to 250 ms (Fluent: 83 / 167 / 250 ms); entrances ease out, exits are quicker;
  nothing may rely on motion alone (WCAG 2.2 SC 2.3.3, "reduced motion").
- Progress should say what is happening with honest, steady numbers (Seow, "good faith" estimates; Mozilla download-manager notes).

## Audit of the palette (WCAG contrast, both themes)
All text pairs pass 4.5:1: primary 15.6, secondary 8.7 to 9.1, muted 5.3 to 6.4, accent link 6.3 to 7.1,
accent-button label 6.8 to 7.1, error text 6.8 to 8.1. The only miss was the dark progress track (1.11:1 against the card):
now `#272B32` (1.25:1; the filled part is the information, the track only shows how much remains).

## Research added in v3 (2026-11-19)
- **Empty states** (NN/g, Smashing, EUI, Mintlify content guide): say what will appear, give the next step in under 15 words per message,
  use an encouraging "yet", use ONE visual (more makes the page crowded), and make it work without the visual.
- **Windows window guidance** (Microsoft Learn, "Window management" and "Screen sizes"): a window must be entirely visible and sized to fit
  the work area; a resizable window may be designed for a bigger screen but is sized down at display time; test 800x600 at 100 %,
  1024x768 at 125 %, 1200x900 at 150 %; sizes and spacing in multiples of 4.

## Rules
0. **The window fits the screen.** Designed for 980 x 700, but `WindowFit` (Core, tested) sizes it to at most 92 % of the work area and
   leaves room for the title bar, then centres it; minimum 760 x 460 (and lower on a tiny screen). Never taller than the screen, at any scaling.
1. **Hierarchy.** One thing per card is loud: the speed number (20 px semibold) while downloading, the primary button when a decision is needed.
   Finished cards recede (title in secondary text); failed cards get a red edge, a red status line and one accent button (Retry).
2. **Surfaces.** Window canvas, then card (one step lighter in dark, white with a 1 px soft shadow in light), then overlays (menus, toast: lighter again).
   Cards have a 1 px quiet contour; a card that is downloading gets a faint accent edge.
3. **Progressive disclosure.** At rest a card shows: glyph, title, status line, metrics, ONE primary button, quality badge.
   On hover or keyboard focus the badge gives way, in the same place, to log / cancel / remove. Nothing moves; the primary button stays flush right.
4. **State is never colour alone.** A 36 px rounded tile tinted with the state colour holds the glyph (download, sync, pause, check, warning, cancel);
   the words in the status line carry the state too. The main action of a running card is a quiet round Pause button (icon only, tooltip and
   screen-reader name); decisions (Resume, Retry) are labelled accent buttons.
5. **Numbers.** Tabular figures for every changing number; speed and ETA sit in a fixed-width block so neighbours never jitter.
6. **Typography.** Inter (bundled, OFL; falls back to the system UI font): 20 / 14 / 13 / 12.5 / 12 / 11.5, weights 400 and 600 (500 for tabs).
   Titles 14.5 medium; status 13; metadata 12 muted; never below 11.5.
7. **Motion.** Colour changes 120 ms; hover border 150 ms; revealed actions 150 ms; progress value 250 ms ease-out; toast 180 ms fade + 8 px rise.
   No looping decoration (the only continuous motion is the indeterminate bar, which means "working").
8. **Keyboard.** Every control reachable by Tab; a 2 px ring in the text colour (clear on every surface and on the accent button); a card's actions are revealed by focus.
9. **Radii and spacing.** 12 px cards and panels, 8 px controls, 6 px small buttons, pill chips; 4 / 8 / 10 / 12 / 16 / 18 / 22 spacing.
10. **Empty state.** One tile with the download icon, "No downloads yet", "Start one from your browser:", three numbered steps
    (Open a video, Click Download, Watch it appear here), one muted line (supported sites, downloads keep running).
11. **Voice.** Plain words ("The site wants to confirm you're not a bot"), the next step in the hint, the raw text only behind "Show details".

## For the WPF port (4.6c)
Same tokens (read `ThemePalette`), same icon geometries (`Icon.*` in main's App.xaml), same states and spacing. WPF-specific:
`DropShadowEffect` for the light-theme card shadow, `Storyboard`/triggers for the 150 to 250 ms transitions, `FocusVisualStyle` for the ring,
`IsMouseOver` + `IsKeyboardFocusWithin` for the reveal. Bundle the same three Inter files (`Assets/Fonts`) and the OFL text.

Also for WPF (4.6c): call `WindowFit.Compute(...)` with `SystemParameters.WorkArea` (it is in DIPs already: pass scale 1) before the first show;
main's WPF window has a fixed size today and overflows small screens the same way.
