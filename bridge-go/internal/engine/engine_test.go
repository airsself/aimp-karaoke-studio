package engine

import(
	"context"
	"errors"
	"path/filepath"
	"testing"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/cache"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/config"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)
type fake struct{ name string; rows []model.Candidate; err error; calls int}
func(f *fake)Name()string{return f.name}
func(f *fake)Search(_ context.Context,_ model.Track)([]model.Candidate,error){f.calls++;return f.rows,f.err}
func TestRankingAndCache(t *testing.T) {
	song:=model.Track{Artist:"Demo",Title:"Night"}
	p1:=&fake{name:"plain",rows:[]model.Candidate{{Track:song,Source:"plain",Content:"hello world",Synced:false}}}
	p2:=&fake{name:"timed",rows:[]model.Candidate{{Track:song,Source:"timed",Content:"[00:01.00]hello world",Synced:true}}}
	cfg:=config.Default()
	cacheDB:=cache.New(filepath.Join(t.TempDir(),"cache.json"),10)
	e:=New(cfg,cacheDB,p1,p2)
	r,err:=e.Search(context.Background(),song,false)
	if err!=nil||r.Best==nil||r.Best.Source!="timed"{t.Fatalf("ranking %+v %v",r,err)}
	_,err=e.Search(context.Background(),song,false)
	if err!=nil||p1.calls!=1||p2.calls!=1{t.Fatal("cache missed")}
	_,err=e.Search(context.Background(),song,true)
	if err!=nil||p1.calls!=2{t.Fatal("forced lookup reused cache")}
}
func TestProviderErrorsNotNegativelyCached(t *testing.T){
	song:=model.Track{Artist:"A",Title:"B"}
	p:=&fake{name:"bad",err:errors.New("offline")}
	e:=New(config.Default(),cache.New(filepath.Join(t.TempDir(),"cache.json"),10),p)
	_,_ = e.Search(context.Background(),song,false)
	_,_ = e.Search(context.Background(),song,false)
	if p.calls!=2{t.Fatal("provider failures poisoned negative cache")}
}
