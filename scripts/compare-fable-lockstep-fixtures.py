#!/usr/bin/env python3
"""Compare canonical fixture bytes and identify the first divergent record."""

from __future__ import annotations

import sys
from pathlib import Path


def record_at(data: bytes, offset: int) -> tuple[int | None, int | None]:
    cursor = 0
    while cursor + 4 <= len(data):
        size = int.from_bytes(data[cursor : cursor + 4], "little")
        end = cursor + 4 + size
        if offset < end:
            if cursor + 12 <= len(data):
                case_id = int.from_bytes(data[cursor + 4 : cursor + 8], "little")
                operation = int.from_bytes(data[cursor + 8 : cursor + 10], "little")
                return case_id, operation
            break
        cursor = end
    return None, None


def context(data: bytes, offset: int) -> str:
    start = max(0, offset - 8)
    end = min(len(data), offset + 9)
    return data[start:end].hex()


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: compare-fable-lockstep-fixtures.py EXPECTED ACTUAL", file=sys.stderr)
        return 2

    expected_path, actual_path = map(Path, sys.argv[1:])
    expected = expected_path.read_bytes()
    actual = actual_path.read_bytes()

    shared = min(len(expected), len(actual))
    offset = next((index for index in range(shared) if expected[index] != actual[index]), shared)

    if expected == actual:
        print(
            f"lockstep fixtures: exact — {actual_path.name} "
            f"({len(actual)} canonical bytes)"
        )
        return 0

    case_id, operation = record_at(expected if offset < len(expected) else actual, offset)
    print(
        "lockstep fixture divergence: "
        f"case={case_id if case_id is not None else 'unknown'} "
        f"operation={operation if operation is not None else 'unknown'} "
        f"byteOffset={offset} expectedLength={len(expected)} actualLength={len(actual)}",
        file=sys.stderr,
    )
    print(f"expected context: {context(expected, offset)}", file=sys.stderr)
    print(f"actual context:   {context(actual, offset)}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
