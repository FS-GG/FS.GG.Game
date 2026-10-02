#![no_std]

// Bounded conformance subset adapted from FS-GG/FS.GG.SC2.Client
// 57eadf00bb9e776664e00fe200bb4f2952f72829
// sdk/rust/sc2c-sdk/src/lib.rs (arena and frozen ABI macro).
use core::{panic::PanicInfo, ptr};

#[panic_handler]
fn panic(_: &PanicInfo) -> ! { loop {} }

#[repr(align(4))]
struct Arena([u8; 65536]);
static mut ARENA: Arena = Arena([0; 65536]);
static mut NEXT: usize = 0;
static OUTPUT: [u8; 4] = *b"SC2C";

#[no_mangle] pub extern "C" fn sc2c_abi_version() -> u32 { 0x0001_0000 }
#[no_mangle] pub extern "C" fn sc2c_alloc(length: u32) -> u32 {
    if length == 0 { return 0; }
    unsafe {
        let start = (NEXT + 3) & !3;
        let Some(end) = start.checked_add(length as usize) else { return 0; };
        if end > 65536 { return 0; }
        NEXT = end;
        ptr::addr_of_mut!(ARENA.0).cast::<u8>().add(start) as u32
    }
}
#[no_mangle] pub extern "C" fn sc2c_free(pointer: u32, length: u32) {
    if pointer == 0 || length == 0 { return; }
    unsafe {
        let base = ptr::addr_of_mut!(ARENA.0).cast::<u8>() as usize;
        if let Some(offset) = (pointer as usize).checked_sub(base) {
            if offset.checked_add(length as usize) == Some(NEXT) { NEXT = offset; }
        }
    }
}
fn invoke(input: u32, length: u32, descriptor: u32) -> i32 {
    if input == 0 || length == 0 || descriptor == 0 { return 2; }
    unsafe {
        ptr::write_unaligned(descriptor as *mut u32, OUTPUT.as_ptr() as u32);
        ptr::write_unaligned((descriptor + 4) as *mut u32, OUTPUT.len() as u32);
    }
    0
}
#[no_mangle] pub extern "C" fn sc2c_initialize(input: u32, length: u32, descriptor: u32) -> i32 { invoke(input, length, descriptor) }
#[no_mangle] pub extern "C" fn sc2c_process(input: u32, length: u32, descriptor: u32) -> i32 { invoke(input, length, descriptor) }
#[no_mangle] pub extern "C" fn sc2c_shutdown() -> i32 { unsafe { NEXT = 0 }; 0 }
