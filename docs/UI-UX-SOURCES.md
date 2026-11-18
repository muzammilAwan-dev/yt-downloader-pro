# UI/UX research sources

Retrieved/reviewed 15 November 2026. Curated for this **WPF Windows utility + Chromium extension**, not an exhaustive collection of every design rule or community skill.

The installed skills contain original project guidance and links. No third-party source code, artwork, fonts, skill scripts or component libraries were installed. No analytics are required for research; use consensual local task observations and owner feedback.

## Source register

| ID | Source | Application and limits |
| --- | --- | --- |
| S01 | [Microsoft: Windows app best practices](https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices) | Native interactions, windowing/Snap, themes, layout, scaling and accessibility. Updated 7 Oct 2026. Some APIs are WinUI-specific; verify WPF support before implementation. |
| S02 | [Fluent 2: design tokens](https://fluent2.microsoft.design/design-tokens) | Separate raw values from semantic aliases; centralize typography/color/spacing/elevation. Principles, not a mandate to add Fluent React packages. |
| S03 | [Fluent 2: typography](https://fluent2.microsoft.design/typography) | Scannable hierarchy, system font families, platform-specific ramps and sentence case. Windows and web ramps differ; WPF uses device-independent units, not CSS units. |
| S04 | [Fluent 2: accessibility](https://fluent2.microsoft.design/accessibility) | Structure, focus, contrast, scaling and meaningful text. Page describes WCAG 2.1 AA component foundations; this project's web benchmark is the later WCAG 2.2 AA. Custom WPF styles inherit no compliance guarantee. |
| S05 | [Fluent 2: motion](https://fluent2.microsoft.design/motion) | Explain changes with quick, intentional transitions; support no-motion alternatives. No universal duration is prescribed here. |
| S06 | [Fluent 2: content design](https://fluent2.microsoft.design/content-design) | User-centered plain language, active voice, sentence case, concise headings, useful alternatives and localization-ready wording. |
| S07 | [W3C: WCAG 2.2](https://www.w3.org/TR/WCAG22/) | Normative web accessibility criteria and conformance requirements. Retrieved Recommendation dated 12 Dec 2024. Applies directly to extension web content; use as an engineering benchmark for WPF, not native certification. |
| S08 | [W3C: target size (minimum), SC 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html) | Informative explanation of the AA 24 x 24 CSS-pixel minimum and exceptions, including spacing. Updated 2 Nov 2026. A blanket 44-pixel AA requirement would be incorrect. |
| S09 | [W3C APG: modal dialog](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/) | Web dialog labeling, focus entry, containment, Escape and restoration. Do not paste ARIA into XAML; do not treat all popovers as modals. |
| S10 | [Jakob Nielsen / NNGroup: 10 usability heuristics](https://www.nngroup.com/articles/ten-usability-heuristics/) | System visibility, familiar language, control, consistency, prevention, recognition, efficiency, restraint, recovery and help. Broad evaluation heuristics, not normative standards or evidence that this app has a problem. Last reviewed 30 Jan 2024. Credit Jakob Nielsen for this framework. |
| S11 | [Jakob Nielsen / NNGroup: progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/) | Keep frequently needed controls visible; make advanced options discoverable. Published 3 Dec 2006; retained because the pattern remains applicable, not because it is a new trend. |
| S12 | [Microsoft: WPF custom-control UI Automation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control) | Automation peers, actual control patterns, accessible properties and change events. Border/Grid/Canvas alone do not supply interactive peers. Historical partial-trust discussion is not an instruction to add legacy CAS to .NET 8. |
| S13 | [Microsoft: WPF control performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls) | Bound-list UI virtualization, recycling and template cost; documents that CanContentScroll=false disables virtualization. Verify current behavior/measurement before changing scrolling or state ownership. |
| S14 | [Chrome: add a popup](https://developer.chrome.com/docs/extensions/develop/ui/add-popup) | Popup is transient and closes when focus moves outside; scripts must be separate. Do not keep long-running work solely in popup memory. |
| S15 | [Anthropic: frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md) | Reviewed as a creative-workflow reference: subject-specific choices, compact design plan, restrained signature detail and screenshot self-critique. NOT installed verbatim or used as an authority over Windows conventions. Website hero/layout advice does not fit a download queue. Mutable upstream reference, not a pinned dependency. |
| S16 | [OpenCode V2: skills](https://opencode.ai/docs/skills) | Project `.opencode/skills/<id>/SKILL.md`, path-derived IDs, name/description frontmatter and on-demand loading. |
| S17 | [OpenCode V2: config](https://opencode.ai/docs/config) | Project configuration and instruction loading. No global provider/credential changes needed. |
| S18 | [OpenCode V2: commands](https://opencode.ai/docs/commands) | Current-session prompt commands; no shell expansion, background agents or model overrides needed. |

## Research translated into project rules
1. **State honesty** (S01, S10): show the real task stage. Byte transfer finishing is not the same as merge/publication completing; never fabricate percentage or ETA.
2. **Stable information hierarchy** (S02, S03, S10): title + state lead; speed/progress and relevant next actions follow. Layout should not jump whenever a value changes.
3. **Accessible native controls** (S01, S04, S12): preserve semantics while styling. A pretty clickable Border is not a replacement for a Button.
4. **Measurable accessibility** (S04, S07, S08): ordinary text at least 4.5:1; qualifying large text 3:1; meaningful UI/focus visuals 3:1 where required. Test rendered backgrounds and all interaction states; not every decorative separator needs 3:1.
5. **Progressive disclosure** (S11): keep pause/resume/cancel and completion actions easy to find. Technical logs and uncommon configuration belong in clear secondary surfaces.
6. **Control without ambiguity** (S06, S10): cancelling a download, removing a history entry and deleting a media file are different actions. Explain actual retention behavior and do not promise undo if none exists.
7. **Purposeful personality** (S01, S05, S15): choose a signature detail from this product's task, not trendy decoration. Preserve familiar window behavior and reduced-motion fallbacks.
8. **Performance is UX** (S01, S13): measure list scrolling and update cost with many tasks. UI virtualization is not data virtualization; recycled expansion state must belong to the item.
9. **Cross-surface continuity** (S02, S06, S14): share vocabulary, semantic roles and visual rhythm, not identical layouts. Extension feedback must distinguish handoff requested from host acknowledgement/completion.

## Explicit distinctions
- WCAG is a testable web standard. Fluent/Windows guidance is platform guidance. Nielsen heuristics are research-informed rules of thumb. Art direction, exact spacing and animation duration are project choices to test with users.
- Proposed 4/8-based spacing, larger comfortable action targets or a restrained accent palette are choices, not universal requirements.
- Browser CSS-pixel reflow requirements are not literal WPF window breakpoints. Validate WPF against real window size, display DPI and user text settings.
- This setup does not establish accessibility conformance, user preference, performance gains or successful live downloads.
- Outdated repository images are explicitly excluded from current-design conclusions (owner correction).

## Retrieval note
The guessed WPF `advanced/accessibility-best-practices` URL returned 404. It was replaced through online lookup with S12; no claim was based on the missing page.
