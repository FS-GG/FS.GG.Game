#![no_std]
use core::panic::PanicInfo;
use fsgg_wasm_guest::{allocate, free_last, SC2_ABI_VERSION, write_descriptor};
#[panic_handler] fn panic(_: &PanicInfo) -> ! { loop {} }
#[repr(align(4))] struct Storage([u8; 65536]);
static mut STORAGE: Storage = Storage([0; 65536]);
static mut NEXT: usize = 0;
static OUTPUT: [u8; 4] = *b"SC23";
#[no_mangle] pub extern "C" fn sc2c_abi_version() -> u32 { SC2_ABI_VERSION }
#[no_mangle] pub extern "C" fn sc2c_alloc(length:u32)->u32 { unsafe { allocate(core::ptr::addr_of_mut!(STORAGE.0).cast(),65536,core::ptr::addr_of_mut!(NEXT),length) } }
#[no_mangle] pub extern "C" fn sc2c_free(pointer:u32,length:u32){ unsafe { free_last(core::ptr::addr_of_mut!(STORAGE.0).cast(),core::ptr::addr_of_mut!(NEXT),pointer,length) } }
fn invoke(input:u32,length:u32,descriptor:u32)->i32 { if input==0||length==0||descriptor==0{return 2} write_descriptor(descriptor,OUTPUT.as_ptr() as u32,OUTPUT.len() as u32);0 }
#[no_mangle] pub extern "C" fn sc2c_initialize(p:u32,n:u32,d:u32)->i32{invoke(p,n,d)}
#[no_mangle] pub extern "C" fn sc2c_process(p:u32,n:u32,d:u32)->i32{invoke(p,n,d)}
#[no_mangle] pub extern "C" fn sc2c_shutdown()->i32{unsafe{NEXT=0};0}
