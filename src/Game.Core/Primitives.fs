namespace FS.GG.Game.Core

type Point = { X: float; Y: float }

type Rect =
    { X: float
      Y: float
      Width: float
      Height: float }

type Contact = { Normal: Point; Depth: float }

type Circle = { Center: Point; Radius: float }
