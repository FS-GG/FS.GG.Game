namespace FS.GG.Game.Reference

open FS.GG.UI.Scene
open FS.GG.Game.Render

/// Worked example (FS.GG.Game#795): a symbology generated from a sample game-state type modelled on the
/// template's arena (`fs-gg-fable-game`: players in cells, a collectible, a hazard, a goal, health and score), rendered
/// in every preset. The view reads the theme through a reader, so a switch keeps the game state.
module ArenaExample =

    open Symbology

    type Role =
        | Runner
        | Guard
        | Scout

    type Rank =
        | Rookie
        | Veteran
        | Elite

    /// A player as the arena's state holds it.
    type Player =
        {
            Name: string
            Cell: int * int
            Allegiance: Identity
            Team: int
            Role: Role
            Health: float
            Rank: Rank
            Carrying: bool
        }

    type FeatureKind =
        | Goal
        | Hazard
        | Pickup
        | Spawn

    /// A fixed arena feature.
    type Feature = { Kind: FeatureKind; Cell: int * int }

    /// The game's state: the server-authoritative arena plus the client's selection. The theme is not in it.
    type ArenaState = { Players: Player list; Features: Feature list; Selected: string option }

    /// The roster: team 0 is this client's (own); the other three teams are opponents and share the enemy allegiance, so
    /// the team badge is mandatory (glyph ADR D1).
    let roster = [ Identity.Own; Identity.Enemy; Identity.Enemy; Identity.Enemy ]

    /// The players' symbology, as a game author writes it.
    let playerDraft: Draft<Player> =
        Symbology.create<Player> "unit" 24.0
        |> Encode.identity "allegiance" (fun p -> p.Allegiance) Frame
        |> Encode.team "team" (fun p -> p.Team) roster TeamMark
        |> Encode.fraction "health" (fun p -> p.Health) (Amplifier Below)
        |> Encode.ordinal "rank" (fun p -> p.Rank) [ Rookie; Veteran; Elite ] (Amplifier Above)
        |> Encode.flag "carrying" (fun p -> p.Carrying) Modifier
        |> Encode.label "name" (fun p -> p.Name)

    /// The same author's first attempt, which also puts the role on the icon: the build refuses it, because the
    /// mandatory badge takes the inner centre of a 24-unit frame (Root's #794 merge comment).
    let playerDraftWithRoleIcon: Draft<Player> =
        playerDraft |> Encode.nominal "role" (fun p -> p.Role) [ Runner; Guard; Scout ] Icon

    /// The features' symbology: a neutral frame with the kind on the icon.
    let featureDraft: Draft<Feature> =
        Symbology.create<Feature> "structure" 24.0
        |> Encode.identity "allegiance" (fun _ -> Identity.Neutral) Frame
        |> Encode.nominal "kind" (fun f -> f.Kind) [ Goal; Hazard; Pickup; Spawn ] Icon

    let tile = 48.0
    let columns = 6
    let rows = 3
    let width = tile * float columns
    let height = tile * float rows

    /// A sample state: four teams, every identity the roster uses, three ranks, health from low to full.
    let sample: ArenaState =
        {
            Players =
                [
                    { Name = "Ada"; Cell = 0, 0; Allegiance = Identity.Own; Team = 0; Role = Runner; Health = 0.9; Rank = Elite; Carrying = true }
                    { Name = "Bo"; Cell = 2, 0; Allegiance = Identity.Enemy; Team = 1; Role = Guard; Health = 0.5; Rank = Veteran; Carrying = false }
                    { Name = "Cy"; Cell = 4, 0; Allegiance = Identity.Enemy; Team = 2; Role = Scout; Health = 0.2; Rank = Rookie; Carrying = false }
                    { Name = "Di"; Cell = 1, 1; Allegiance = Identity.Enemy; Team = 3; Role = Runner; Health = 1.0; Rank = Veteran; Carrying = true }
                    { Name = "Ed"; Cell = 3, 1; Allegiance = Identity.Enemy; Team = 1; Role = Guard; Health = 0.7; Rank = Rookie; Carrying = false }
                ]
            Features = [ { Kind = Goal; Cell = 5, 1 }; { Kind = Hazard; Cell = 0, 2 }; { Kind = Pickup; Cell = 2, 2 }; { Kind = Spawn; Cell = 4, 2 } ]
            Selected = Some "Bo"
        }

    let private centre (col: int, row: int) = { X = tile * float col + tile / 2.0; Y = tile * float row + tile / 2.0 + 2.0 }

    let private unwrap (result: Result<'T, SymbologyError list>) =
        match result with
        | Ok s -> s
        | Error errors -> failwithf "symbology refused: %s" (errors |> List.map explain |> String.concat "; ")

    /// The game's view: the state and a theme reader in, a Scene out. The symbologies are built once against every
    /// preset the player may choose; whether team colours show is decided once per scene.
    let view (themes: Theme list) : (unit -> Theme) -> ArenaState -> Scene =
        let players = Symbology.build themes playerDraft |> unwrap
        let features = Symbology.build themes featureDraft |> unwrap
        fun read state ->
            let theme = read ()
            let teamColours = Symbology.teamColouring theme players
            let glyphs =
                [
                    for f in state.Features -> Symbology.glyph false false (Symbology.spec features (centre f.Cell) f |> unwrap)
                    for p in state.Players -> Symbology.glyph teamColours (state.Selected = Some p.Name) (Symbology.spec players (centre p.Cell) p |> unwrap)
                ]
            let drawn = FS.GG.Game.Render.Glyphs.scene theme glyphs
            { Nodes = SceneNode.Rectangle((0.0, 0.0, width, height), (Theme.tryColour "surface.ground" theme |> Option.get)) :: drawn.Nodes }

    /// The mirror labels of the sample (the accessible names a game's mirror would show).
    let labels (themes: Theme list) (state: ArenaState) : string list =
        let players = Symbology.build themes playerDraft |> unwrap
        state.Players |> List.map (Symbology.label players >> unwrap)
