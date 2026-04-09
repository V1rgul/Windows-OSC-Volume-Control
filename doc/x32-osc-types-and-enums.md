# X32 OSC Types And Enums

Source PDF: `E:\Downloads\X32-OSC(1).pdf`

Note on page numbering: the PDF's internal page numbers are offset from the printed page numbers shown in the document header. The "types" material the document labels as page 10 starts on PDF page 11 and continues through PDF pages 12-14.

## Types Section

From the section titled `X32/M32 OSC Protocol Parameters` (PDF page 11, printed page 10):

- `types -> [string, enum(integer), int(integer), linf(float), logf(float), level(float), bitmap(integer)]`

Type definitions extracted from that page:

- `string`: A string of characters padded to a multiple of 4 with `\0` (null) characters.
- `enum`: An int corresponding to an element in a `[list of all possible strings]`.
- `int`: An int with value in `[min. value, max. value]`, step size `= 1`.
- `linf`: A float with value in `[min. value, max. value, step size]`, following a linear scale.
- `logf`: A float with value in `[min. value, max. value, steps]`, following a log scale.
- `level`: A float with value in `[-90.0...10.0 (+10 dB), steps]` over 4 "linear" dB ranges:
  - `0.0...0.0625` -> `(-oo, -90...-60 dB)`
  - `0.0625...0.25` -> `(-60...-30 dB)`
  - `0.25...0.5` -> `(-30...-10 dB)`
  - `0.5...1.0` -> `(-10...+10 dB)`
- `%int` / bitmap integer: An int corresponding to the bitwise OR of multiple bits (`0` or `1`).

## Quick Summary

- `string`
  - Free text values; no subtype catalog is currently tracked in this doc.
- `enum`
  - Boolean-like:
    - `{OFF, ON}`
  - Routing / mode sets:
    - `{PFL, AFL}`
    - `{INT, EXT}`
    - `{F1, F2}`
    - `{SINE, PINK, WHITE}`
    - `{REC, PLAY}`
    - `{LR+M, LCR}`
    - `{OFF, LR, LR+C, LRPFL, LRAFL, AUX56, AUX78}`
  - Color set:
    - `{OFF, RD, GN, YE, BL, MG, CY, WH, OFFi, RDi, GNi, YEi, BLi, MGi, CYi, WHi}`
  - Dynamics / gate sets:
    - `{12, 18, 24}`
    - `{EXP2, EXP3, EXP4, GATE, DUCK}`
    - `{LC6, LC12, HC6, HC12, 1.0, 2.0, 3.0, 5.0, 10.0}`
    - `{COMP, EXP}`
    - `{PEAK, RMS}`
    - `{LIN, LOG}`
    - `{1.1, 1.3, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 7.0, 10, 20, 100}`
    - `{PRE, POST}`
  - Insert / EQ / mix sets:
    - `{OFF, FX1L, FX1R, FX2L, FX2R, FX3L, FX3R, FX4L, FX4R, FX5L, FX5R, FX6L, FX6R, FX7L, FX7R, FX8L, FX8R, AUX1, AUX2, AUX3, AUX4, AUX5, AUX6}`
    - `{LCut, LShv, PEQ, VEQ, HShv, HCut}`
    - `{IN/LC, <-EQ, EQ->, PRE, POST, GRP}`
    - `{IN/LC, <-EQ, EQ->, PRE, POST}`
    - `{OFF, X, Y}`
    - `{LCut, LShv, PEQ, VEQ, HShv, HCut, BU6, BU12, BS12, LR12, BU18, BU24, BS24, LR24}`
  - Routing source sets:
    - `{AN1-8, AN9-16, AN17-24, AN25-32, A1-8, A9-16, A17-24, A25-32, A33-40, A41-48, B1-8, B9-16, B17-24, B25-32, B33-40, B41-48, CARD1-8, CARD9-16, CARD17-24, CARD25-32, UIN1-8, UIN9-16, UIN17-24, UIN25-32}`
    - `{AUX1-4, AN1-2, AN1-4, AN1-6, A1-2, A1-4, A1-6, B1-2, B1-4, B1-6, CARD1-2, CARD1-4, CARD1-6, UIN1-2, UIN1-4, UIN1-6}`
- `int`
  - Raw integer values; this doc only calls out true enums and repeated range families, not every integer-only parameter.
- `linf`
  - Ranges used in this doc:
    - `[-18.000, 18.000, 0.500] dB`
    - `[-18.000, 18.000, 0.250] dB`
    - `[-40.000, 0.000, 1.000] dB`
    - `[0.300, 500.000, 0.100] ms`
    - `[-6.000, 24.000, 0.500] dB`
    - `[-80.000, 0.000, 0.500] dB`
    - `[3.000, 60.000, 1.000] dB`
    - `[0.000, 120.000, 1.000] ms`
    - `[-60.000, 0.000, 0.500] dB`
    - `[0.000, 5.000, 1.000]`
    - `[0.000, 24.000, 0.500] dB`
    - `[0, 100, 5] %`
    - `[-15.000, 15.000, 0.250] dB`
    - `[-100.000, 100.000, 2.000]`
    - `[-12.000, 12.000, 0.500]`
- `logf`
  - Ranges used in this doc:
    - `[20.000, 20000, 121] Hz`
    - `[20.000, 400.000, 101] Hz`
    - `[0.020, 2000, 101] ms`
    - `[5.000, 4000.000, 101] ms`
    - `[20.000, 20000, 201] Hz`
    - `[10.000, 0.3, 72]`
- `level`
  - Ranges used in this doc:
    - `[-90.0…10.0 (+10 dB), 161]`
    - `[0.0…1.0 (+10 dB), 1024]`
    - `[0.0…1.0 (+10 dB), 161]`
- `bitmap` / `%int`
  - Bitmask values; no subtype catalog is currently tracked in this doc.

## Type Rules And Formatting

From `Type rules (Get/Set parameter) and data formatting` (PDF pages 12-14, printed pages 11-13):

- X32/M32 follows OSC 1.06 with the basic OSC type tags for `int32`, `float32`, `string`, and `blob`.
- Float parameters in standard OSC parameter messages are normalized to `0.0 - 1.0`.
- Enum parameters can be sent as either strings or integers.
- Boolean parameters map to enum type `{OFF, ON}` or OSC integer `{0, 1}`.
- Blobs follow section-specific rules.

### Enum-specific note

The document explicitly says enum values can be sent as either strings or integers.

Example given:

- `/ch/01/gate/mode` has possible values `{EXP2, EXP3, EXP4, GATE, DUCK}`
- Setting `GATE` can be sent as:
  - string form: `/ch/01/gate/mode,s GATE`
  - integer form: `/ch/01/gate/mode,i 3`

The document also warns this only applies to true `enum` parameters, not parameters typed as plain `int`.

## Detailed Address Mapping

The detailed subtype-to-address mapping has been moved to `doc/x32-osc-type-address-details.md`.

This main file now keeps:

- the type overview
- subtype summaries
- conversion notes and formulas

Use `doc/x32-osc-type-address-details.md` when you need the per-subtype address lists.

## `enum`

The supported enum families are summarized in `Quick Summary`.

Detailed address mapping: `doc/x32-osc-type-address-details.md`

## `level`

`level` is not a single logarithmic formula like `logf`. It is a piecewise dB mapping with a special mute point at `0.0`.

Forward mapping from normalized OSC float `t` to dB:

- if `t <= 0.0`, treat as mute / `-oo`
- if `0.0 < t <= 0.0625`, `dB = -90 + 480 * t`
- if `0.0625 < t <= 0.25`, `dB = -60 + 160 * (t - 0.0625)`
- if `0.25 < t <= 0.5`, `dB = -30 + 80 * (t - 0.25)`
- if `0.5 < t <= 1.0`, `dB = -10 + 40 * (t - 0.5)`

Inverse mapping from dB back to normalized OSC float:

- if muted / `-oo`, use `t = 0.0`
- if `-90 <= dB <= -60`, `t = (dB + 90) / 480`
- if `-60 < dB <= -30`, `t = 0.0625 + (dB + 60) / 160`
- if `-30 < dB <= -10`, `t = 0.25 + (dB + 30) / 80`
- if `-10 < dB <= 10`, `t = 0.5 + (dB + 10) / 40`

Practical notes:

- This matches the 161-step appendix table: `0.0000 -> -oo`, `0.0625 -> -60`, `0.2500 -> -30`, `0.5000 -> -10`, `0.7500 -> 0`, `1.0000 -> +10`.
- The curve shape is piecewise-linear in dB; the difference between `161` and `1024` variants is the number of discrete supported steps, not a different curve family.
- If exact console parity is important, quantize to the nearest supported step count for the specific subtype after applying the piecewise formula.

Detailed address mapping: `doc/x32-osc-type-address-details.md`

## `linf`

`linf` uses linear interpolation per subtype range `[min, max, step]`.

Detailed address mapping: `doc/x32-osc-type-address-details.md`

## `logf`

For a `logf` subtype with range `[min, max, steps]`, a generated formula is usually enough.

Continuous form over normalized OSC float range `[0.0, 1.0]`:

- `t = clamp(normalized, 0.0, 1.0)`
- `value = min * (max / min) ^ t`

Discrete form for the console-supported step set:

- `i = round(t * (steps - 1))`
- `value_i = min * (max / min) ^ (i / (steps - 1))`

Inverse mapping from a real-world value back to normalized OSC space:

- `t = log(value / min) / log(max / min)`

Practical notes:

- `logf` subtypes do not all share the same `min`, `max`, or `steps`, but they do follow the same logarithmic interpolation pattern.
- So you do not need a handwritten lookup table from the PDF for every `logf` subtype, as long as you have that subtype's `[min, max, steps]`.
- If exact console parity is important, precompute the discrete table from the formula above and round to the nearest generated entry.

Detailed address mapping: `doc/x32-osc-type-address-details.md`
