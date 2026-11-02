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

            test "a grid follows the APG: rows by aria-rowindex across rowgroups, row edges stop, Home and End per row" {
                // Rowgroups: the ungrouped a and b (rows 1, 2), Units (u: row 3), Map (t00 t02 at row 4, t12 at row 5).
                let items =
                    itemsOf
                        [
                            "a", ObjectAccess.full
                            "u", ObjectAccess.grouped "Units"
                            "t00", ObjectAccess.cell "Map" 0 0
                            "t02", ObjectAccess.cell "Map" 0 2
                            "t12", ObjectAccess.cell "Map" 1 2
                            "b", ObjectAccess.full
                        ]

                let layout = InputLayout.layout "Board objects" items
                let move key from = InputLayout.navigate key false (Some from) layout items

                Expect.equal (move "ArrowDown" "a") (Some "b") "down to the next row of the same rowgroup"
                Expect.equal (move "ArrowDown" "b") (Some "u") "down crosses into the next rowgroup"
                Expect.equal (move "ArrowDown" "u") (Some "t00") "down into the cell group, same column"
                Expect.equal (move "ArrowRight" "t00") (Some "t02") "right skips the empty column"
                Expect.equal (move "ArrowRight" "t02") (Some "t02") "right stops at the row's end"
                Expect.equal (move "ArrowLeft" "t00") (Some "t00") "left stops at the row's start: no wrap"
                Expect.equal (move "ArrowDown" "t00") (Some "t12") "down to the nearest cell of a sparse row"
                Expect.equal (move "ArrowDown" "t12") (Some "t12") "down stops at the last row"
                Expect.equal (move "ArrowUp" "t00") (Some "u") "up leaves the cell group"
                Expect.equal (move "Home" "t02") (Some "t00") "Home is the row's first cell"
                Expect.equal (move "End" "t00") (Some "t02") "End is the row's last cell"
                Expect.equal (InputLayout.navigate "Home" true (Some "t12") layout items) (Some "a") "Control+Home is the grid's first cell"
                Expect.equal (InputLayout.navigate "End" true (Some "a") layout items) (Some "t12") "Control+End is the grid's last cell"
                Expect.equal (move "PageDown" "a") (Some "u") "Page Down moves to the next rowgroup"
                Expect.equal (move "PageUp" "t00") (Some "u") "Page Up moves to the previous rowgroup's first cell"
            }

            test "outside a cell group arrows move in object order, wrapping; no focus starts at an end" {
                let items =
                    itemsOf [ "a", ObjectAccess.full; "b", ObjectAccess.pointerOnly; "c", ObjectAccess.full ]

                let layout = InputLayout.layout "Board objects" items
                let nav key focused = InputLayout.navigate key false focused layout items
                Expect.equal (nav "ArrowRight" (Some "a")) (Some "c") "skips pointer-only b"
                Expect.equal (nav "ArrowDown" (Some "c")) (Some "a") "wraps forward"
                Expect.equal (nav "ArrowUp" (Some "a")) (Some "c") "wraps backward"
                Expect.equal (nav "ArrowRight" None) (Some "a") "forward from nothing: first"
                Expect.equal (nav "ArrowLeft" None) (Some "c") "backward from nothing: last"
                Expect.equal (nav "ArrowRight" (Some "b")) (Some "a") "an unreachable focus restarts"
                Expect.equal (InputLayout.navigate "ArrowRight" false None (InputLayout.Layout.Listbox []) []) None "no item, no focus"
                Expect.isTrue (InputLayout.isNavigation "PageDown") "Page Down navigates"
                Expect.isFalse (InputLayout.isNavigation "Enter") "Enter does not navigate"
            }
        ]
