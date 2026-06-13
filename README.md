# SVGoblin

A research project exploring raster-to-vector conversion in C# / .NET 9: it takes a
`.png` image as input and produces a vectorized `.svg` file.

## Principles

1. **No third-party dependencies.** The vectorization algorithm is implemented from
   scratch in this repository. The only references are the .NET base class library and
   Microsoft-maintained platform packages (currently `System.Drawing.Common`, used for
   PNG decoding and raw pixel access). No external vectorization libraries or wrappers.
2. **Research first, but aimed at real use.** The goal is a *usable* PNG-to-vector
   converter, not a toy. Algorithms start simple and are iterated on, but the output
   should always be a valid SVG that renders correctly.

## How it works

The pipeline is orchestrated by `Vectorizer` in [Vectorizer.cs](Vectorizer.cs):

1. **Color quantization** — [ColorQuantizer.cs](ColorQuantizer.cs) detects the
   background (transparent pixels, or the color sampled at the image corners), builds
   a small flat-color palette from pixels with a *flat neighborhood* (anti-aliased
   ramp pixels are excluded because their surroundings are not flat), then assigns
   every pixel to the nearest of background/palette. The result is one binary mask
   per color; anti-aliased ramps split cleanly at their midpoint. `BlackWhite` mode
   skips this and thresholds to a single layer.
2. **Contour tracing** — [ContourTracer.cs](ContourTracer.cs) walks the "cracks"
   between foreground and background pixels, producing closed polygons of lattice
   points that bound each region exactly. Outer boundaries and holes come out with
   opposite winding, so a `nonzero` fill rule renders holes (e.g. the inside of an
   outlined circle) correctly.
3. **Smoothing & simplification** — [PathSimplifier.cs](PathSimplifier.cs) first
   smooths each contour with a small binomial filter (`ContourSmoothPasses`) to
   remove the ±1 px staircase/quantization wobble, then collapses it with
   Ramer–Douglas–Peucker, bounded by `SimplifyTolerance` pixels of deviation.
   Shapes smaller than `MinShapeArea` are dropped.
4. **Curve generation** — [CurveFitter.cs](CurveFitter.cs) keeps vertices with a
   turn angle above `CornerAngleThreshold` as sharp corners and pins stretches
   whose underlying contour hugs a chord as true line segments (so straight
   strokes stay straight). The smooth stretches in between are converted per
   `CurveMode`: either fitted with Schneider's algorithm (least-squares fit with
   Newton–Raphson reparameterization, recursively splitting until the error is
   below `CurveTolerance`) or interpolated with a centripetal Catmull-Rom spline.
   Line/curve junctions stay tangent-continuous.
5. **SVG generation** — [SvgWriter.cs](SvgWriter.cs) emits one `<path>` per color
   layer (all of its loops as subpaths) with the quantized fill color.

## Usage

### Command line

```bash
SVGoblin [input.png|folder] [output.svg|folder] [schneider|catmullrom|lines] [--recursive]
```

The first positional argument is required; the rest are optional:

| Position | Default | Description |
|---|---|---|
| 1 | *(required)* | Input PNG file, **or a folder** to batch-convert (see below). |
| 2 | `./output.svg` | Output SVG file; in folder mode, an output folder (created if missing). |
| 3 | `schneider` | Curve engine: `schneider`, `catmullrom`, or `lines` (no curve fitting). |

| Flag | Description |
|---|---|
| `--recursive` | Folder mode only: also convert `.png` files in subfolders. |

#### Folder mode

When the input is a folder, every `.png` file in it is converted to an `.svg`
with the same base name. Without an output folder, each `.svg` is written next
to its source `.png`; with one, the converted files go there instead. With
`--recursive`, `.png` files in subfolders are included, and an output folder
mirrors the input's subfolder structure.

#### Examples

Convert a PNG with the default Schneider curve fitting:

```bash
SVGoblin logo.png logo.svg
```

Use Catmull-Rom spline interpolation instead (the curves pass through the
traced contour points, which can track wobbly hand-drawn shapes more closely):

```bash
SVGoblin sketch.png sketch.svg catmullrom
```

Skip curve fitting entirely and emit straight line segments only (fastest;
useful for pixel art or for inspecting the raw simplified contours):

```bash
SVGoblin icon.png icon.svg lines
```

Convert every PNG in a folder, writing each SVG next to its source file:

```bash
SVGoblin ./icons
```

Convert a whole folder tree into a separate output folder (subfolder
structure is mirrored), using Catmull-Rom curves:

```bash
SVGoblin ./icons ./vectors catmullrom --recursive
```

During development you can run it through `dotnet run`, passing the same
arguments after `--`:

```bash
dotnet run -- logo.png logo.svg catmullrom
```

On success the program prints a short summary and exits with code `0`:

```text
Vectorized logo.png -> logo.svg
  4 color layer(s), 9 loop(s), 87 segment(s)
```

In folder mode each converted file gets its own summary line, followed by a
total (`Converted 11 of 12 file(s)`). A file that fails to convert is reported
on stderr and the batch continues with the remaining files.

A missing or invalid input, an unknown curve engine, or an unknown flag prints an error
to stderr and exits with code `1`. Folder mode also exits with code `1` when
any file failed to convert or the folder contained no `.png` files at all.

Other options (palette size, tolerances, black/white mode, …) are not exposed
on the command line yet — use the library API below to set them.

### From code

```csharp
var vectorizer = new Vectorizer(new VectorizerOptions
{
    MaxColors = 8,
    CurveTolerance = 1.5,
});

vectorizer.VectorizeToDisk("input.png", "output.svg");
```

### Options

| Option | Default | Description |
|---|---|---|
| `Mode` | `Color` | `Color` traces a layer per quantized color; `BlackWhite` thresholds to one black layer. |
| `MaxColors` | `8` | Maximum palette size in `Color` mode. |
| `ColorTolerance` | `48` | RGB distance within which colors count as the same flat color. |
| `Threshold` | `128` | Grayscale cutoff (0–255), `BlackWhite` mode only. |
| `SimplifyTolerance` | `1.0` | Max deviation (px) when simplifying traced boundaries. |
| `ContourSmoothPasses` | `2` | Binomial smoothing passes on traced contours; removes staircase noise (~0.35 px corner rounding per pass). `0` disables. |
| `EnableCurveFitting` | `true` | Fit cubic Béziers; `false` emits line segments. |
| `CurveMode` | `Schneider` | Curve engine: `Schneider` least-squares fitting or `CatmullRom` centripetal spline interpolation. |
| `CurveTolerance` | `1.5` | Max curve-fitting error in pixels. |
| `CornerAngleThreshold` | `60` | Min turn angle (degrees) for a vertex to stay a sharp corner. |
| `MinShapeArea` | `4` | Minimum shape area (px²); smaller specks are dropped. |
| `BackgroundColor` | `null` | Optional background fill; `null` keeps it transparent. |

## Known limitations

- **Windows only (for now).** Pixel access goes through `System.Drawing`/GDI+, which
  is Windows-only since .NET 7. Going cross-platform under the no-third-party rule
  would mean decoding PNG ourselves.
- **Flat colors only.** Quantization targets icon/logo-style art; photographs and
  gradients will posterize into at most `MaxColors` flat layers.
- **Thin features can lose their color.** Strokes narrower than ~2 px never have a
  flat neighborhood, so they may be absorbed into the nearest palette color.
