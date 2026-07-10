namespace FS.GG.Game.Core

type Point = { X: float; Y: float }

type Rect =
    { X: float
      Y: float
      Width: float
      Height: float }

type Contact = { Normal: Point; Depth: float }

type Circle = { Center: Point; Radius: float }

type RayHit = { T: float; Point: Point; Normal: Point }

type ConvexPolygon = { Vertices: Point[] }

type Manifold =
    { A: int
      B: int
      Normal: Point
      Depth: float
      Points: Point[]
      PointCount: int
      FeatureId: int }
