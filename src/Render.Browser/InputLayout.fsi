namespace FS.GG.Game.Render.Browser

open FS.GG.Game.Render

/// The pure half of the input host (FS.GG.Game#788, ADR 0001 §4): which objects the keyboard and the accessible mirror
/// reach, how the mirror is structured (one Tab stop: a listbox, or a grid when any cell group exists), and where the
/// navigation keys move the focus. No browser dependency, so the .NET tests check it directly.
module internal InputLayout =

    /// One keyboard-reachable object, as the mirror offers it.
    type Item =
        {
            /// `SelectableObject.Id`.
            Id: string
            /// `SelectableObject.Label`.
            Label: string
            /// The group label of a grouped or cell object.
            Group: string option
            /// The (row, column) of a cell, when its group is a cell group (every member declares a cell).
            Cell: (int * int) option
        }

    /// One entry of a listbox surface: an option, or a labelled group of options.
    [<RequireQualifiedAccess>]
    type Entry =
        | Option of item: Item
        | Group of label: string * items: Item list

    /// One gridcell: its item, its accessible name (the group's name is part of it) and its `aria-colindex` (from 1).
    type GridCell = { Item: Item; Name: string; ColIndex: int }

    /// One row of a grid: its `aria-rowindex` (from 1, over the whole grid) and its cells in column order.
    type GridRow = { RowIndex: int; Cells: GridCell list }

    /// One labelled rowgroup of a grid.
    type RowGroup = { Label: string; Rows: GridRow list }

    /// The surface's structure.
    [<RequireQualifiedAccess>]
    type Layout =
        /// No cell group: a `role=listbox`.
        | Listbox of entries: Entry list
        /// Any cell group: one `role=grid` with its `aria-rowcount` and `aria-colcount`.
        | Grid of groups: RowGroup list * rowCount: int * colCount: int

    /// The keyboard-reachable objects in object order. A group is a cell group only when every member declares a cell;
    /// otherwise its members' cells are ignored and it is a grouped set.
    val items: access: (SelectableObject -> ObjectAccess) -> objects: SelectableObject list -> Item list

    /// The surface structure of `items`. `otherLabel` names the one rowgroup that holds every ungrouped object on a grid.
    val layout: otherLabel: string -> items: Item list -> Layout

    /// Whether `key` (a `KeyboardEvent.key` value) is one of the navigation keys: the arrows, Home, End, Page Up and
    /// Page Down.
    val isNavigation: key: string -> bool

    /// Where navigation key `key` moves the focus from `focused` over `items`: arrows move to the next or previous
    /// item (wrapping), or in two dimensions inside a cell group (stopping at its edge, skipping gaps); Home and End
    /// jump to the ends; Page Up and Page Down move to the first item of the previous or next group (ungrouped objects
    /// are one group). With no focus (or a focus no longer reachable), forward keys go to the first item and backward
    /// keys to the last. `None` when there is no item.
    val navigate: key: string -> focused: string option -> items: Item list -> string option
