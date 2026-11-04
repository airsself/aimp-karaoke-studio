//go:build windows

package process

import "syscall"

// Wait on a Windows process handle, so a WPF crash cannot leave an orphan
// HTTP server serving until the next login. Never inject into another process.
func Watch(parentPID int,stop func()){
	if parentPID<=0{return}
	kernel:=syscall.NewLazyDLL("kernel32.dll")
	open:=kernel.NewProc("OpenProcess")
	wait:=kernel.NewProc("WaitForSingleObject")
	closeHandle:=kernel.NewProc("CloseHandle")
	const SYNCHRONIZE=0x00100000
	handle,_,_:=open.Call(SYNCHRONIZE,0,uintptr(parentPID))
	if handle==0{stop();return}
	defer closeHandle.Call(handle)
	wait.Call(handle,0xFFFFFFFF)
	stop()
}
