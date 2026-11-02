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

    let navigate (key: string) (focused: string option) (items: Item list) : string option =
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

                // Inside a cell group the arrows move in two dimensions: the nearest cell in that direction on the same
                // row (or column), skipping gaps, and nothing past the group's edge.
                let move (along: int * int -> int) (across: int * int -> int) (forward: bool) =
                    match current.Cell with
                    | Some here ->
                        let candidates =
                            items
                            |> List.filter (fun item ->
                                item.Group = current.Group
                                && item.Cell
                                   |> Option.exists (fun cell ->
                                       across cell = across here
                                       && (if forward then along cell > along here else along cell < along here)))
                            |> List.sortBy (fun item -> along (Option.get item.Cell))

                        match (if forward then List.tryHead candidates else List.tryLast candidates) with
                        | Some next -> Some next.Id
                        | None -> Some current.Id
                    | None -> linear (if forward then 1 else -1)

                let groups = groupOrder items

                let groupStep step =
                    let at = groups |> List.findIndex (fun group -> group = current.Group)
                    let target = at + step

                    if target < 0 || target >= List.length groups then
                        Some current.Id
                    else
                        members groups.[target] items |> List.head |> fun item -> Some item.Id

                match key with
                | "ArrowRight" -> move snd fst true
                | "ArrowLeft" -> move snd fst false
                | "ArrowDown" -> move fst snd true
                | "ArrowUp" -> move fst snd false
                | "Home" -> Some first.Id
                | "End" -> Some last.Id
                | "PageDown" -> groupStep 1
                | "PageUp" -> groupStep -1
                | _ -> Some current.Id
