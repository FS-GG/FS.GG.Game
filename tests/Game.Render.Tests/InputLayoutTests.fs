module Game.Render.Tests.InputLayoutTests

// FS.GG.Game#788: the pure half of the browser input host (src/Render.Browser/InputLayout.fs, compiled into this
// project as a link): which objects the keyboard and mirror reach, the single-Tab-stop listbox or grid structure (ADR
// 0001 §4 and Root's #793 carry-overs: the group name in each cell's name, aria-rowindex/aria-colindex for sparse
// groups, ONE rowgroup for every ungrouped object) and where the navigation keys move the focus (2D in a cell group).
// The browser smoke reads the same structure back from the DOM of every backend.

open Expecto
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser

let private target id : SelectableObject =
    {
        Id = id
        Label = "Label " + id
        Bounds = { X = 0.0; Y = 0.0; Width = 10.0; Height = 10.0 }
        Shape = SelectableShape.Box
    }

let private itemsOf (declared: (string * ObjectAccess) list) =
    let access = Map.ofList declared
    InputLayout.items (fun o -> access.[o.Id]) (declared |> List.map (fst >> target))

let private ids (items: InputLayout.Item list) = items |> List.map (fun item -> item.Id)

[<Tests>]
let tests =
    testList
        "InputLayout"
        [
            test "pointerOnly objects have no item; a group with a non-cell member is a grouped set" {
                let items =
                    itemsOf
                        [
                            "a", ObjectAccess.full
                            "b", ObjectAccess.pointerOnly
                            "c", ObjectAccess.cell "Mixed" 0 0
                            "d", ObjectAccess.grouped "Mixed"
                        ]

                Expect.equal (ids items) [ "a"; "c"; "d" ] "pointer-only b has no item"
                Expect.isTrue (items |> List.forall (fun item -> item.Cell.IsNone)) "a mixed group has no cells"
            }

            test "without a cell group the surface is a listbox, grouped sets nested in groups" {
                let items =
                    itemsOf
                        [
                            "a", ObjectAccess.full
                            "g1", ObjectAccess.grouped "Units"
                            "b", ObjectAccess.full
                            "g2", ObjectAccess.grouped "Units"
                        ]

                match InputLayout.layout "Board objects" items with
                | InputLayout.Layout.Listbox entries ->
                    let shape =
                        entries
                        |> List.map (fun entry ->
                            match entry with
                            | InputLayout.Entry.Option item -> item.Id
                            | InputLayout.Entry.Group(label, members) -> label + "[" + String.concat "," (ids members) + "]")

                    Expect.equal shape [ "a"; "Units[g1,g2]"; "b" ] "options and one labelled group, in first-appearance order"
                | other -> failtestf "expected a listbox, got %A" other
            }

            test "any cell group makes one grid: sparse indices, group names in cell names, one rowgroup for the rest" {
                let items =
                    itemsOf
                        [
                            "x", ObjectAccess.full
                            "t00", ObjectAccess.cell "Map" 0 0
                            "t02", ObjectAccess.cell "Map" 0 2
                            "t21", ObjectAccess.cell "Map" 2 1
                            "y", ObjectAccess.full
                            "u", ObjectAccess.grouped "Units"
                        ]

                match InputLayout.layout "Board objects" items with
                | InputLayout.Layout.Grid(groups, rowCount, colCount) ->
                    let shape =
                        groups
                        |> List.map (fun group ->
                            group.Label
                            + ":"
                            + (group.Rows
                               |> List.map (fun row ->
                                   string row.RowIndex
                                   + "("
                                   + (row.Cells
                                      |> List.map (fun cell -> sprintf "%s@%d=%s" cell.Item.Id cell.ColIndex cell.Name)
                                      |> String.concat " ")
                                   + ")")
                               |> String.concat " "))

                    Expect.equal
                        shape
                        [
                            "Board objects:1(x@1=Label x) 2(y@1=Label y)"
                            "Map:3(t00@1=Label t00, Map t02@3=Label t02, Map) 5(t21@2=Label t21, Map)"
                            "Units:6(u@1=Label u, Units)"
                        ]
                        "ungrouped objects share ONE rowgroup; the cell group keeps its sparse row 2 as aria-rowindex 5"

                    Expect.equal (rowCount, colCount) (6, 3) "aria-rowcount and aria-colcount"
                | other -> failtestf "expected a grid, got %A" other
            }

            test "arrows move in two dimensions inside a cell group, skipping gaps and stopping at the edge" {
                let items =
                    itemsOf
                        [
                            "t00", ObjectAccess.cell "Map" 0 0
                            "t02", ObjectAccess.cell "Map" 0 2
                            "t10", ObjectAccess.cell "Map" 1 0
                            "t12", ObjectAccess.cell "Map" 1 2
                            "u", ObjectAccess.grouped "Units"
                        ]

                let move key from = InputLayout.navigate key (Some from) items

                Expect.equal (move "ArrowRight" "t00") (Some "t02") "right skips the gap at column 1"
                Expect.equal (move "ArrowRight" "t02") (Some "t02") "right stops at the edge"
                Expect.equal (move "ArrowDown" "t02") (Some "t12") "down keeps the column"
                Expect.equal (move "ArrowUp" "t00") (Some "t00") "up stops at the edge"
                Expect.equal (move "ArrowLeft" "t12") (Some "t10") "left skips the gap"
                Expect.equal (move "PageDown" "t00") (Some "u") "Page Down moves to the next group"
                Expect.equal (move "PageUp" "u") (Some "t00") "Page Up moves to the previous group's first item"
                Expect.equal (move "Home" "t12") (Some "t00") "Home is the first item"
                Expect.equal (move "End" "t00") (Some "u") "End is the last item"
            }

            test "outside a cell group arrows move in object order, wrapping; no focus starts at an end" {
                let items =
                    itemsOf [ "a", ObjectAccess.full; "b", ObjectAccess.pointerOnly; "c", ObjectAccess.full ]

                Expect.equal (InputLayout.navigate "ArrowRight" (Some "a") items) (Some "c") "skips pointer-only b"
                Expect.equal (InputLayout.navigate "ArrowDown" (Some "c") items) (Some "a") "wraps forward"
                Expect.equal (InputLayout.navigate "ArrowUp" (Some "a") items) (Some "c") "wraps backward"
                Expect.equal (InputLayout.navigate "ArrowRight" None items) (Some "a") "forward from nothing: first"
                Expect.equal (InputLayout.navigate "ArrowLeft" None items) (Some "c") "backward from nothing: last"
                Expect.equal (InputLayout.navigate "ArrowRight" (Some "b") items) (Some "a") "an unreachable focus restarts"
                Expect.equal (InputLayout.navigate "ArrowRight" None []) None "no item, no focus"
                Expect.isTrue (InputLayout.isNavigation "PageDown") "Page Down navigates"
                Expect.isFalse (InputLayout.isNavigation "Enter") "Enter does not navigate"
            }
        ]
