#![no_std]

use core::ptr;

pub const BAR_ABI_VERSION: u32 = 1;
pub const SC2_ABI_VERSION: u32 = 0x0001_0000;
pub const DESCRIPTOR_BYTES: u32 = 8;

pub unsafe fn allocate(bytes: *mut u8, capacity: usize, next: *mut usize, length: u32) -> u32 {
    if length == 0 { return 0; }
    let start = ((*next) + 3) & !3;
    let Some(end) = start.checked_add(length as usize) else { return 0; };
    if end > capacity { return 0; }
    *next = end;
    bytes.add(start) as u32
}

pub unsafe fn free_last(bytes: *mut u8, next: *mut usize, pointer: u32, length: u32) {
    if pointer == 0 || length == 0 { return; }
    let base = bytes as usize;
    if let Some(offset) = (pointer as usize).checked_sub(base) {
        if offset.checked_add(length as usize) == Some(*next) { *next = offset; }
    }
}

pub fn write_descriptor(descriptor: u32, output: u32, length: u32) {
    unsafe {
        ptr::write_unaligned(descriptor as *mut u32, output.to_le());
        ptr::write_unaligned(descriptor.wrapping_add(4) as *mut u32, length.to_le());
    }
}
