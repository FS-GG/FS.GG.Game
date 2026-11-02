namespace FS.GG.Game.Render.Browser

open FS.GG.Game.Render

module internal InputLayout =

    type Item =
        {
            Id: string
            Label: string
            Group: string option
            Cell: (int * int) option
        }

    [<RequireQualifiedAccess>]
    type Entry =
        | Option of item: Item
        | Group of label: string * items: Item list

    type GridCell = { Item: Item; Name: string; ColIndex: int }

    type GridRow = { RowIndex: int; Cells: GridCell list }

    type RowGroup = { Label: string; Rows: GridRow list }

    [<RequireQualifiedAccess>]
    type Layout =
        | Listbox of entries: Entry list
        | Grid of groups: RowGroup list * rowCount: int * colCount: int

    let items (access: SelectableObject -> ObjectAccess) (objects: SelectableObject list) : Item list =
        let declared =
            objects
            |> List.map (fun target -> target, access target)
            |> List.filter (fun (_, declared) -> ObjectAccess.isKeyboardReachable declared)

        // A group is a cell group only when every member declares a cell.
        let cellGroups =
            declared
            |> List.choose (fun (_, declared) -> ObjectAccess.group declared |> Option.map (fun label -> label, declared))
            |> List.groupBy fst
            |> List.filter (fun (_, members) ->
                members |> List.forall (fun (_, declared) -> (ObjectAccess.cellOf declared).IsSome))
            |> List.map fst
            |> Set.ofList

        declared
        |> List.map (fun (target, declared) ->
            let group = ObjectAccess.group declared

            {
                Id = target.Id
                Label = target.Label
                Group = group
                Cell =
                    match group with
                    | Some label when cellGroups.Contains label -> ObjectAccess.cellOf declared
                    | _ -> None
            })

    /// The groups in order of first appearance: `Some label`, or `None` for the ungrouped objects together.
    let private groupOrder (items: Item list) : string option list = items |> List.map (fun item -> item.Group) |> List.distinct

    let private members (group: string option) (items: Item list) =
        items |> List.filter (fun item -> item.Group = group)

    let private isCellGroup (group: string option) (items: Item list) =
        group.IsSome && members group items |> List.forall (fun item -> item.Cell.IsSome)

    let layout (otherLabel: string) (items: Item list) : Layout =
        let groups = groupOrder items

        if items |> List.exists (fun item -> item.Cell.IsSome) then
            let mutable offset = 0
            let mutable columns = 1

            let rowGroups =
                groups
                |> List.map (fun group ->
                    let inGroup = members group items

                    let name (item: Item) =
                        match group with
                        | Some label -> item.Label + ", " + label
                        | None -> item.Label

                    if isCellGroup group items then
                        let cellOf (item: Item) = item.Cell |> Option.defaultValue (0, 0)
                        let span = (inGroup |> List.map (cellOf >> fst) |> List.max) + 1
                        columns <- max columns ((inGroup |> List.map (cellOf >> snd) |> List.max) + 1)

                        let rows =
                            inGroup
                            |> List.groupBy (cellOf >> fst)
                            |> List.sortBy fst
                            |> List.map (fun (row, cells) ->
                                {
                                    RowIndex = offset + row + 1
                                    Cells =
                                        cells
                                        |> List.sortBy (cellOf >> snd)
                                        |> List.map (fun item ->
                                            {
                                                Item = item
                                                Name = name item
                                                ColIndex = snd (cellOf item) + 1
                                            })
                                })

                        offset <- offset + span

                        {
                            Label = group |> Option.defaultValue otherLabel
                            Rows = rows
                        }
                    else
                        let rows =
                            inGroup
                            |> List.mapi (fun index item ->
                                {
                                    RowIndex = offset + index + 1
                                    Cells =
                                        [
                                            {
                                                Item = item
                                                Name = name item
                                                ColIndex = 1
                                            }
                                        ]
                                })

                        offset <- offset + List.length inGroup

                        {
                            Label = group |> Option.defaultValue otherLabel
                            Rows = rows
                        })

            Layout.Grid(rowGroups, offset, columns)
        else
            let mutable seen = Set.empty

            items
            |> List.choose (fun item ->
                match item.Group with
                | None -> Some(Entry.Option item)
                | Some label when seen.Contains label -> None
                | Some label ->
                    seen <- seen.Add label
                    Some(Entry.Group(label, members item.Group items)))
            |> Layout.Listbox

    let private navigationKeys =
        set [ "ArrowRight"; "ArrowLeft"; "ArrowDown"; "ArrowUp"; "Home"; "End"; "PageUp"; "PageDown" ]

    let isNavigation (key: string) = navigationKeys.Contains key

    /// Every grid cell with its (aria-rowindex, aria-colindex), in DOM order, and the rowgroup it is in.
    let private cells (groups: RowGroup list) =
        [
            for groupIndex, group in List.indexed groups do
                for row in group.Rows do
                    for cell in row.Cells do
                        yield cell.Item.Id, (row.RowIndex, cell.ColIndex), groupIndex
        ]

    /// The grid keyboard of the ARIA Authoring Practices: Left and Right move along the row and stop at its ends, Up and
    /// Down move to the nearest cell of the previous or next row (by `aria-rowindex`, across rowgroups, the same column
    /// when it has one), Home and End go to the ends of the row (with Control, of the grid), and Page Up and Page Down
    /// move to the first cell of the previous or next rowgroup.
    let private navigateGrid (key: string) (control: bool) (focused: string option) (groups: RowGroup list) =
        let all = cells groups

        match all with
        | [] -> None
        | (firstId, _, _) :: _ ->
            let lastId, _, _ = List.last all
            let backward = key = "ArrowLeft" || key = "ArrowUp" || key = "End"

            match focused |> Option.bind (fun id -> all |> List.tryFind (fun (cell, _, _) -> cell = id)) with
            | None -> Some(if backward then lastId else firstId)
            | Some(here, (row, column), group) ->
                let inRow = all |> List.filter (fun (_, (r, _), _) -> r = row) |> List.sortBy (fun (_, (_, c), _) -> c)
                let idOf (id, _, _) = id

                let vertical (pick: int -> bool) (nearest: int list -> int option) =
                    let rows = all |> List.map (fun (_, (r, _), _) -> r) |> List.distinct |> List.filter pick

                    match nearest rows with
                    | None -> Some here
                    | Some target ->
                        all
                        |> List.filter (fun (_, (r, _), _) -> r = target)
                        |> List.sortBy (fun (_, (_, c), _) -> abs (c - column), c)
                        |> List.head
                        |> idOf
                        |> Some

                let groupFirst (index: int) =
                    all |> List.tryFind (fun (_, _, g) -> g = index) |> Option.map idOf |> Option.orElse (Some here)

                match key with
                | "ArrowRight" ->
                    inRow |> List.tryFind (fun (_, (_, c), _) -> c > column) |> Option.map idOf |> Option.orElse (Some here)
                | "ArrowLeft" ->
                    inRow |> List.filter (fun (_, (_, c), _) -> c < column) |> List.tryLast |> Option.map idOf |> Option.orElse (Some here)
                | "ArrowDown" -> vertical (fun r -> r > row) (fun rows -> if rows.IsEmpty then None else Some(List.min rows))
                | "ArrowUp" -> vertical (fun r -> r < row) (fun rows -> if rows.IsEmpty then None else Some(List.max rows))
                | "Home" when control -> Some firstId
                | "End" when control -> Some lastId
                | "Home" -> inRow |> List.head |> idOf |> Some
                | "End" -> inRow |> List.last |> idOf |> Some
                | "PageDown" -> if group + 1 < groups.Length then groupFirst (group + 1) else Some here
                | "PageUp" -> if group > 0 then groupFirst (group - 1) else Some here
                | _ -> Some here

    let private navigateList (key: string) (focused: string option) (items: Item list) : string option =
        match items with
        | [] -> None
        | first :: _ ->
            let last = List.last items
            let backward = key = "ArrowLeft" || key = "ArrowUp" || key = "End"

            match focused |> Option.bind (fun id -> items |> List.tryFindIndex (fun item -> item.Id = id)) with
            | None -> Some(if backward then last.Id else first.Id)
            | Some index ->
                let current = items.[index]
                let count = List.length items
                let linear step = Some items.[(index + step + count) % count].Id
                let groups = groupOrder items

                let groupStep step =
                    let at = groups |> List.findIndex (fun group -> group = current.Group)
                    let target = at + step

                    if target < 0 || target >= List.length groups then
                        Some current.Id
                    else
                        members groups.[target] items |> List.head |> fun item -> Some item.Id

                match key with
                | "ArrowRight"
                | "ArrowDown" -> linear 1
                | "ArrowLeft"
                | "ArrowUp" -> linear -1
                | "Home" -> Some first.Id
                | "End" -> Some last.Id
                | "PageDown" -> groupStep 1
                | "PageUp" -> groupStep -1
                | _ -> Some current.Id

    let navigate (key: string) (control: bool) (focused: string option) (layout: Layout) (items: Item list) : string option =
        match layout with
        | Layout.Grid(groups, _, _) -> navigateGrid key control focused groups
        | Layout.Listbox _ -> navigateList key focused items
