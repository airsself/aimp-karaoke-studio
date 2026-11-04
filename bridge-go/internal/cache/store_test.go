package cache

import ("path/filepath";"testing";"time";"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model")
func TestPersistentTTLAndNegative(t *testing.T){
	path:=filepath.Join(t.TempDir(),"cache.json")
	track:=model.Track{Artist:"Singer",Title:"Song"}
	s:=New(path,2)
	best:=&model.Candidate{Track:track,Content:"line",Source:"test",Score:1}
	if err:=s.Put(track,best,time.Hour);err!=nil{t.Fatal(err)}
	again:=New(path,2)
	e,ok:=again.Get(track)
	if !ok||e.Best==nil||e.Best.Content!="line"{t.Fatal("cache not persistent")}
	missing:=model.Track{Artist:"Other",Title:"Missing"}
	// Deterministic clock: Windows filesystem sync latency must not expire
	// a negative entry before the first assertion.
	start:=time.Now()
	again.now=func()time.Time{return start}
	if err:=again.Put(missing,nil,time.Minute);err!=nil{t.Fatal(err)}
	if e,ok:=again.Get(missing);!ok||e.Best!=nil{t.Fatal("negative entry missing")}
	again.now=func()time.Time{return start.Add(2*time.Minute)}
	if _,ok:=again.Get(missing);ok{t.Fatal("expired negative cache was used")}
	if Key(track)==Key(missing){t.Fatal("cache key collision")}
}
