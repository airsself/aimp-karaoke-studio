// AIMP Karaoke Studio v4.1 — original, open-source Go lyrics bridge.
// Reimplements only the publicly documented v2.5 marker wire contract.
package main

import(
	"context"
	"flag"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strconv"
	"strings"
	"time"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/cache"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/config"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/engine"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/httpapi"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/process"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/provider"
)
func main() {
	if err:=run();err!=nil{log.Printf("Go Bridge: %v",err);os.Exit(1)}
}
func run()error{
	path:=flag.String("config","","bridge JSON config, defaults when omitted")
	listen:=flag.String("listen","","override loopback address")
	parent:=flag.Int("parent-pid",0,"exit when this parent application closes")
	flag.Parse()
	cfg,err:=config.Load(*path);if err!=nil{return err}
	if *listen!="" {cfg.Listen=*listen}
	if err=cfg.Validate();err!=nil{return err}
	client:=provider.NewClient(cfg.Timeout())
	var sources []provider.Provider
	seen:=map[string]bool{}
	for _,name:=range cfg.ProviderOrder{
		if seen[name]{continue};seen[name]=true
		switch name{
		case "lrclib":if cfg.LRCLIBEnabled {
			sources=append(sources,provider.LRCLIB{Client:client,BaseURL:cfg.LRCLIBBaseURL})
		}
		case "lyrics.ovh":if cfg.LyricsOVHEnabled {
			sources=append(sources,provider.LyricsOVH{Client:client,BaseURL:cfg.LyricsOVHBaseURL})
		}
		default:log.Printf("provider %s not implemented (binary-only reference); skipped",name)
		}
	}
	if len(sources)==0{return fmt.Errorf("no supported providers enabled")}
	cacheDir:=cfg.CacheDir
	if !filepath.IsAbs(cacheDir) {
		root,err:=os.UserCacheDir()
		if err!=nil{return err}
		cacheDir=filepath.Join(root,"AimpKaraokeStudio","go-bridge")
	}
	store:=cache.New(filepath.Join(cacheDir,"cache-v1.json"),cfg.CacheMaxEntries)
	service:=engine.New(cfg,store,sources...)
	handler:=httpapi.New(service,cfg.MaxLyricsBytes)
	l,err:=bindLoopback(cfg.Listen,20)
	if err!=nil{return err}
	defer l.Close()
	ctx,stop:=signal.NotifyContext(context.Background(),os.Interrupt)
	defer stop()
	if *parent>0 {go process.Watch(*parent,stop)}
	server:=httpapi.NewServer(l,handler)
	go func(){<-ctx.Done();deadline,cancel:=context.WithTimeout(context.Background(),5*time.Second)
		defer cancel();_ = server.Shutdown(deadline)}()
	log.Printf("AIMP open-source Go Bridge 4.1 listening on %s (parent PID: %d)",l.Addr(),*parent)
	err=server.Serve(l)
	if err!=nil&&err!=http.ErrServerClosed{return err}
	return nil
}
func bindLoopback(address string,count int)(net.Listener,error){
	host,portText,err:=net.SplitHostPort(address)
	if err!=nil{return nil,err}
	if host!="127.0.0.1"&&host!="::1"{return nil,fmt.Errorf("non-loopback listen refused")}
	p,err:=strconv.Atoi(portText)
	if err!=nil||p<=0||p>65535{return nil,fmt.Errorf("invalid TCP port")}
	network:="tcp4";if strings.Contains(host,":"){network="tcp6"}
	for i:=0;i<count&&p+i<=65535;i++{
		l,err:=net.Listen(network,net.JoinHostPort(host,strconv.Itoa(p+i)))
		if err==nil{return l,nil}
		if i==count-1{return nil,err}
	}
	return nil,fmt.Errorf("no free loopback listener in %s (+%d)",address,count)
}
