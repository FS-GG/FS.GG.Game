module ThemeNumerics

// Deterministic numerical corpus shared by the production theme probes. No theme implementation lives here.
open FS.GG.UI.Scene
open FS.GG.Game.Render

let rgb r g b : Color = { Red = byte r; Green = byte g; Blue = byte b; Alpha = 255uy }
let hex = ThemeColour.hex

// Independent expression of ADR B4's input grid, used by the browser's conversion corpus.
// The fuzz passes the actual production ThemeGrid.inputOf to both digest functions below.
let referenceInput colour =
    let p = ThemeColour.toOklch colour
    let grid scale x = System.Math.Floor(x * scale + 0.5) / scale
    let c = grid 1e6 p.C
    { L = grid 1e6 p.L; C = c; H = if c < 0.001 then 0.0 else grid 1e4 p.H }

let roundTripFailures () =
    let mutable failures = 0
    for r in 0..255 do
        for g in 0..255 do
            for b in 0..255 do
                let c = rgb r g b
                if ThemeColour.ofOklch (ThemeColour.toOklch c) <> c then failures <- failures + 1
    failures

let fixedText (decimals: int) (value: float) : string =
    let scale = 10.0 ** float decimals
    let scaled = System.Math.Floor(abs value * scale + 0.5)
    let whole = System.Math.Floor(scaled / scale)
    let frac = scaled - whole * scale
    let fracText = string (int64 frac)
    let padded = if decimals > fracText.Length then System.String('0', decimals - fracText.Length) + fracText else fracText
    (if value < 0.0 && scaled > 0.0 then "-" else "") + string (int64 whole) + (if decimals > 0 then "." + padded else "")

let gridDigest (inputOf: Color -> Oklch) : float =
    let okabeIto = [ rgb 230 159 0; rgb 86 180 233; rgb 0 158 115; rgb 240 228 66; rgb 0 114 178; rgb 213 94 0; rgb 204 121 167 ]
    let hues = [ for j in 0..119 -> float j * 3.0 ] @ (okabeIto |> List.map (fun c -> (inputOf c).H))
    let mutable h = 0.0
    let add (v: byte) = h <- (h * 257.0 + float v) % 2147483647.0
    for hue in hues do
        for c in [ 0.0; 0.012; 0.05; 0.06; 0.14 ] do
            for k in 0..10000 do
                let colour = ThemeColour.ofOklch { L = float k / float 10000; C = c; H = hue }
                add colour.Red
                add colour.Green
                add colour.Blue
    h

/// A digest of `inputOf` over all 16,777,216 8-bit colours (their gridded L, C and h, as integers of grid units), so
/// the derivation inputs a ground or seed colour gives are compared between .NET and Fable over the whole domain.
let inputDigest (inputOf: Color -> Oklch) : float =
    let mutable h = 0.0
    let add (v: float) = h <- (h * 257.0 + v) % 2147483647.0
    for r in 0..255 do
        for g in 0..255 do
            for b in 0..255 do
                let i = inputOf (rgb r g b)
                add (System.Math.Floor(i.L * 1e6 + 0.5) % 1000003.0)
                add (System.Math.Floor(i.C * 1e6 + 0.5) % 1000003.0)
                add (System.Math.Floor(i.H * 1e4 + 0.5) % 1000003.0)
    h

