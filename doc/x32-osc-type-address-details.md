# X32 OSC Type Address Details

Source PDF: `E:\Downloads\X32-OSC(1).pdf`

This file contains the detailed subtype-to-address mapping moved out of `doc/x32-osc-types-and-enums.md`.

## `enum`

The FX preset-name appendix has been intentionally omitted here. Those values are effect selector strings, not enum types that this project needs to support.

### `{OFF, ON}`

Associated paths:

- Linking and config:
  `/config/chlink/*`, `/config/auxlink/*`, `/config/fxlink/*`, `/config/buslink/*`, `/config/mtxlink/*`, `/config/mute/[1…6]`, `/config/linkcfg/hadly`, `/config/linkcfg/eq`, `/config/linkcfg/dyn`, `/config/linkcfg/fdrmute`, `/config/mono/link`
- Solo and talkback:
  `/config/solo/exclusive`, `/config/solo/followsel`, `/config/solo/followsolo`, `/config/solo/dim`, `/config/solo/mono`, `/config/solo/delay`, `/config/solo/masterctrl`, `/config/solo/mute`, `/config/solo/dimpfl`, `/config/talk/enable`, `/config/talk/A/dim`, `/config/talk/B/dim`, `/config/talk/A/latch`, `/config/talk/B/latch`
- Channel family:
  `/ch/[01…32]/delay/on`, `/ch/[01…32]/preamp/invert`, `/ch/[01…32]/preamp/hpon`, `/ch/[01…32]/gate/on`, `/ch/[01…32]/gate/filter/on`, `/ch/[01…32]/dyn/on`, `/ch/[01…32]/dyn/auto`, `/ch/[01…32]/dyn/filter/on`, `/ch/[01…32]/insert/on`, `/ch/[01…32]/eq/on`, `/ch/[01…32]/mix/on`, `/ch/[01…32]/mix/st`, `/ch/[01…32]/mix/mono`, `/ch/[01…32]/mix/[01…16]/on`
- Aux in and FX return:
  `/auxin/[01…08]/preamp/invert`, `/auxin/[01…08]/eq/on`, `/auxin/[01…08]/mix/on`, `/auxin/[01…08]/mix/st`, `/auxin/[01…08]/mix/mono`, `/auxin/[01…08]/mix/[01…16]/on`, `/fxrtn/[01…08]/eq/on`, `/fxrtn/[01…08]/mix/on`, `/fxrtn/[01…08]/mix/st`, `/fxrtn/[01…08]/mix/mono`, `/fxrtn/[01…08]/mix/[01…16]/on`
- Bus, matrix, main, DCA:
  `/bus/[01…16]/dyn/on`, `/bus/[01…16]/dyn/auto`, `/bus/[01…16]/dyn/filter/on`, `/bus/[01…16]/insert/on`, `/bus/[01…16]/eq/on`, `/bus/[01…16]/mix/on`, `/bus/[01…16]/mix/st`, `/bus/[01…16]/mix/mono`, `/bus/[01…16]/mix/[01…06]/on`, `/mtx/[01…06]/config/preamp/invert`, `/mtx/[01…06]/dyn/on`, `/mtx/[01…06]/dyn/auto`, `/mtx/[01…06]/dyn/filter/on`, `/mtx/[01…06]/insert/on`, `/mtx/[01…06]/eq/on`, `/mtx/[01…06]/mix/on`, `/main/st/dyn/on`, `/main/st/dyn/auto`, `/main/st/dyn/filter/on`, `/main/st/insert/on`, `/main/st/eq/on`, `/main/st/mix/on`, `/main/st/mix/[01…06]/on`, `/main/m/dyn/on`, `/main/m/dyn/auto`, `/main/m/dyn/filter/on`, `/main/m/insert/on`, `/main/m/eq/on`, `/main/m/mix/on`, `/main/m/mix/[01…06]/on`, `/dca/[1…8]/on`

### `{PFL, AFL}`

Associated paths:

- `/config/solo/chmode`
- `/config/solo/busmode`
- `/config/solo/dcamode`

### `{INT, EXT}`

Associated paths:

- `/config/talk/source`

### `{F1, F2}`

Associated paths:

- `/config/osc/fsel`

### `{SINE, PINK, WHITE}`

Associated paths:

- `/config/osc/type`

### `{REC, PLAY}`

Associated paths:

- `/config/routing/routswitch`

### `{LR+M, LCR}`

Associated paths:

- `/config/mono/mode`

### `{OFF, LR, LR+C, LRPFL, LRAFL, AUX56, AUX78}`

Associated paths:

- `/config/solo/source`

### `{OFF, RD, GN, YE, BL, MG, CY, WH, OFFi, RDi, GNi, YEi, BLi, MGi, CYi, WHi}`

Associated paths:

- `/ch/[01…32]/config/color`
- `/auxin/[01…08]/config/color`
- `/fxrtn/[01…08]/config/color`
- `/bus/[01…16]/config/color`
- `/mtx/[01…06]/config/color`
- `/main/st/config/color`
- `/main/m/config/color`
- `/dca/[1…8]/config/color`

### `{12, 18, 24}`

Associated paths:

- `/ch/[01…32]/preamp/hpslope`

### `{EXP2, EXP3, EXP4, GATE, DUCK}`

Associated paths:

- `/ch/[01…32]/gate/mode`

### `{LC6, LC12, HC6, HC12, 1.0, 2.0, 3.0, 5.0, 10.0}`

Associated paths:

- `/ch/[01…32]/gate/filter/type`
- `/ch/[01…32]/dyn/filter/type`
- `/bus/[01…16]/dyn/filter/type`
- `/mtx/[01…06]/dyn/filter/type`
- `/main/st/dyn/filter/type`
- `/main/m/dyn/filter/type`

### `{COMP, EXP}`

Associated paths:

- `/ch/[01…32]/dyn/mode`
- `/bus/[01…16]/dyn/mode`
- `/mtx/[01…06]/dyn/mode`
- `/main/st/dyn/mode`
- `/main/m/dyn/mode`

### `{PEAK, RMS}`

Associated paths:

- `/ch/[01…32]/dyn/det`
- `/bus/[01…16]/dyn/det`
- `/mtx/[01…06]/dyn/det`
- `/main/st/dyn/det`
- `/main/m/dyn/det`

### `{LIN, LOG}`

Associated paths:

- `/ch/[01…32]/dyn/env`
- `/bus/[01…16]/dyn/env`
- `/mtx/[01…06]/dyn/env`
- `/main/st/dyn/env`
- `/main/m/dyn/env`

### `{1.1, 1.3, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 7.0, 10, 20, 100}`

Associated paths:

- `/ch/[01…32]/dyn/ratio`
- `/bus/[01…16]/dyn/ratio`
- `/mtx/[01…06]/dyn/ratio`
- `/main/st/dyn/ratio`
- `/main/m/dyn/ratio`

### `{PRE, POST}`

Associated paths:

- `/ch/[01…32]/dyn/pos`
- `/ch/[01…32]/insert/pos`
- `/bus/[01…16]/dyn/pos`
- `/bus/[01…16]/insert/pos`
- `/mtx/[01…06]/dyn/pos`
- `/mtx/[01…06]/insert/pos`
- `/main/st/dyn/pos`
- `/main/st/insert/pos`
- `/main/m/dyn/pos`
- `/main/m/insert/pos`

### `{OFF, FX1L, FX1R, FX2L, FX2R, FX3L, FX3R, FX4L, FX4R, FX5L, FX5R, FX6L, FX6R, FX7L, FX7R, FX8L, FX8R, AUX1, AUX2, AUX3, AUX4, AUX5, AUX6}`

Associated paths:

- `/ch/[01…32]/insert/sel`
- `/bus/[01…16]/insert/sel`
- `/mtx/[01…06]/insert/sel`
- `/main/st/insert/sel`
- `/main/m/insert/sel`

### `{LCut, LShv, PEQ, VEQ, HShv, HCut}`

Associated paths:

- `/ch/[01…32]/eq/[1…4]/type`
- `/auxin/[01…08]/eq/[1…4]/type`
- `/fxrtn/[01…08]/eq/[1…4]/type`
- `/bus/[01…16]/eq/[1…6]/type`

### `{IN/LC, <-EQ, EQ->, PRE, POST, GRP}`

Associated paths:

- `/ch/[01…32]/mix/01/type`, `/03/type`, `/05/type`, `/07/type`, `/09/type`, `/11/type`, `/13/type`, `/15/type`
- `/auxin/[01…08]/mix/01/type`, `/03/type`, `/05/type`, `/07/type`, `/09/type`, `/11/type`, `/13/type`, `/15/type`
- `/fxrtn/[01…08]/mix/03/type`, `/05/type`, `/07/type`, `/09/type`, `/11/type`, `/13/type`, `/15/type`

### `{IN/LC, <-EQ, EQ->, PRE, POST}`

Associated paths:

- `/bus/[01…16]/mix/01/type`, `/03/type`, `/05/type`
- `/main/st/mix/01/type`, `/03/type`, `/05/type`
- `/main/m/mix/01/type`, `/03/type`, `/05/type`

### `{OFF, X, Y}`

Associated paths:

- `/ch/[01…32]/automix/group`

### `{LCut, LShv, PEQ, VEQ, HShv, HCut, BU6, BU12, BS12, LR12, BU18, BU24, BS24, LR24}`

Associated paths:

- `/mtx/[01…06]/eq/[1…6]/type`
- `/main/st/eq/[1…6]/type`
- `/main/m/eq/[1…6]/type`

### `{AN1-8, AN9-16, AN17-24, AN25-32, A1-8, A9-16, A17-24, A25-32, A33-40, A41-48, B1-8, B9-16, B17-24, B25-32, B33-40, B41-48, CARD1-8, CARD9-16, CARD17-24, CARD25-32, UIN1-8, UIN9-16, UIN17-24, UIN25-32}`

Associated paths:

- `/config/routing/IN/1-8`
- `/config/routing/IN/9-16`
- `/config/routing/IN/17-24`
- `/config/routing/IN/25-32`

### `{AUX1-4, AN1-2, AN1-4, AN1-6, A1-2, A1-4, A1-6, B1-2, B1-4, B1-6, CARD1-2, CARD1-4, CARD1-6, UIN1-2, UIN1-4, UIN1-6}`

Associated paths:

- `/config/routing/IN/AUX`

## `level`

### `[-90.0…10.0 (+10 dB), 161]`

Associated paths:

- `/config/solo/level`
- `/config/talk/A/level`, `/config/talk/B/level`
- `/config/osc/level`
- `/ch/[01…32]/mix/mlevel`, `/ch/[01…32]/mix/[01…16]/level`
- `/auxin/[01…08]/mix/mlevel`, `/auxin/[01…08]/mix/[01…16]/level`
- `/fxrtn/[01…08]/mix/mlevel`, `/fxrtn/[01…08]/mix/[01…16]/level`

### `[0.0…1.0 (+10 dB), 1024]`

Associated paths:

- `/ch/[01…32]/mix/fader`
- `/auxin/[01…08]/mix/fader`
- `/fxrtn/[01…08]/mix/fader`
- `/bus/[01…16]/mix/fader`
- `/mtx/[01…06]/mix/fader`
- `/main/st/mix/fader`
- `/main/m/mix/fader`
- `/dca/[1…8]/fader`

### `[0.0…1.0 (+10 dB), 161]`

Associated paths:

- `/bus/[01…16]/mix/mlevel`, `/bus/[01…16]/mix/[01…06]/level`
- `/main/st/mix/[01…06]/level`
- `/main/m/mix/[01…06]/level`

## `linf`

### `[-18.000, 18.000, 0.500] dB`

Associated paths:

- `/config/solo/sourcetrim`

### `[-18.000, 18.000, 0.250] dB`

Associated paths:

- `/ch/[01…32]/preamp/trim`
- `/auxin/[01…08]/preamp/trim`

### `[-40.000, 0.000, 1.000] dB`

Associated paths:

- `/config/solo/dimatt`

### `[0.300, 500.000, 0.100] ms`

Associated paths:

- `/config/solo/delaytime`
- `/ch/[01…32]/delay/time`

### `[-6.000, 24.000, 0.500] dB`

Associated paths:

- `/config/tape/gainL`
- `/config/tape/gainR`

### `[-80.000, 0.000, 0.500] dB`

Associated paths:

- `/ch/[01…32]/gate/thr`

### `[3.000, 60.000, 1.000] dB`

Associated paths:

- `/ch/[01…32]/gate/range`

### `[0.000, 120.000, 1.000] ms`

Associated paths:

- `/ch/[01…32]/gate/attack`
- `/ch/[01…32]/dyn/attack`
- `/bus/[01…16]/dyn/attack`
- `/mtx/[01…06]/dyn/attack`
- `/main/st/dyn/attack`
- `/main/m/dyn/attack`

### `[-60.000, 0.000, 0.500] dB`

Associated paths:

- `/ch/[01…32]/dyn/thr`
- `/bus/[01…16]/dyn/thr`
- `/mtx/[01…06]/dyn/thr`
- `/main/st/dyn/thr`
- `/main/m/dyn/thr`

### `[0.000, 5.000, 1.000]`

Associated paths:

- `/ch/[01…32]/dyn/knee`
- `/bus/[01…16]/dyn/knee`
- `/mtx/[01…06]/dyn/knee`
- `/main/st/dyn/knee`
- `/main/m/dyn/knee`

### `[0.000, 24.000, 0.500] dB`

Associated paths:

- `/ch/[01…32]/dyn/mgain`
- `/bus/[01…16]/dyn/mgain`
- `/mtx/[01…06]/dyn/mgain`
- `/main/st/dyn/mgain`
- `/main/m/dyn/mgain`

### `[0, 100, 5] %`

Associated paths:

- `/ch/[01…32]/dyn/mix`
- `/bus/[01…16]/dyn/mix`
- `/mtx/[01…06]/dyn/mix`
- `/main/st/dyn/mix`
- `/main/m/dyn/mix`

### `[-15.000, 15.000, 0.250] dB`

Associated paths:

- `/ch/[01…32]/eq/[1…4]/g`
- `/auxin/[01…08]/eq/[1…4]/g`
- `/fxrtn/[01…08]/eq/[1…4]/g`
- `/bus/[01…16]/eq/[1…6]/g`
- `/mtx/[01…06]/eq/[1…6]/g`
- `/main/st/eq/[1…6]/g`
- `/main/m/eq/[1…6]/g`

### `[-100.000, 100.000, 2.000]`

Associated paths:

- `/ch/[01…32]/mix/pan`
- `/ch/[01…32]/mix/01/pan`, `/03/pan`, `/05/pan`, `/07/pan`, `/09/pan`, `/11/pan`, `/13/pan`, `/15/pan`
- `/auxin/[01…08]/mix/pan`
- `/auxin/[01…08]/mix/01/pan`, `/03/pan`, `/05/pan`, `/07/pan`, `/09/pan`, `/11/pan`, `/13/pan`, `/15/pan`
- `/fxrtn/[01…08]/mix/pan`
- `/fxrtn/[01…08]/mix/03/pan`, `/05/pan`, `/07/pan`, `/09/pan`, `/11/pan`, `/13/pan`, `/15/pan`
- `/bus/[01…16]/mix/pan`
- `/bus/[01…16]/mix/01/pan`, `/03/pan`, `/05/pan`
- `/main/st/mix/pan`
- `/main/st/mix/01/pan`, `/03/pan`, `/05/pan`
- `/main/m/mix/01/pan`, `/03/pan`, `/05/pan`

### `[-12.000, 12.000, 0.500]`

Associated paths:

- `/ch/[01…32]/automix/weight`

## `logf`

### `[20.000, 20000, 121] Hz`

Associated paths:

- `/config/osc/f1`
- `/config/osc/f2`

### `[20.000, 400.000, 101] Hz`

Associated paths:

- `/ch/[01…32]/preamp/hpf`

### `[0.020, 2000, 101] ms`

Associated paths:

- `/ch/[01…32]/gate/hold`
- `/ch/[01…32]/dyn/hold`
- `/bus/[01…16]/dyn/hold`
- `/mtx/[01…06]/dyn/hold`
- `/main/st/dyn/hold`
- `/main/m/dyn/hold`

### `[5.000, 4000.000, 101] ms`

Associated paths:

- `/ch/[01…32]/gate/release`
- `/ch/[01…32]/dyn/release`
- `/bus/[01…16]/dyn/release`
- `/mtx/[01…06]/dyn/release`
- `/main/st/dyn/release`
- `/main/m/dyn/release`

### `[20.000, 20000, 201] Hz`

Associated paths:

- `/ch/[01…32]/dyn/filter/f`
- `/ch/[01…32]/eq/[1…4]/f`
- `/auxin/[01…08]/eq/[1…4]/f`
- `/fxrtn/[01…08]/eq/[1…4]/f`
- `/bus/[01…16]/dyn/filter/f`
- `/bus/[01…16]/eq/[1…6]/f`
- `/mtx/[01…06]/dyn/filter/f`
- `/mtx/[01…06]/eq/[1…6]/f`
- `/main/st/dyn/filter/f`
- `/main/st/eq/[1…6]/f`
- `/main/m/dyn/filter/f`
- `/main/m/eq/[1…6]/f`

### `[10.000, 0.3, 72]`

Associated paths:

- `/ch/[01…32]/eq/[1…4]/q`
- `/auxin/[01…08]/eq/[1…4]/q`
- `/fxrtn/[01…08]/eq/[1…4]/q`
- `/bus/[01…16]/eq/[1…6]/q`
- `/mtx/[01…06]/eq/[1…6]/q`
- `/main/st/eq/[1…6]/q`
- `/main/m/eq/[1…6]/q`
