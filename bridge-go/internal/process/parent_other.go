//go:build !windows

package process

import(
	"os"
	"syscall"
	"time"
)
func Watch(pid int,stop func()){
	if pid<=0{return}
	for {
		p,err:=os.FindProcess(pid)
		if err!=nil{stop();return}
		if err=p.Signal(syscall.Signal(0));err!=nil{stop();return}
		time.Sleep(1*time.Second)
	}
}
