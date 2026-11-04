// Package cache is a filesystem-backed TTL cache with atomic snapshots.
// It never stores audio bytes, local filenames or external API credentials.
package cache

import(
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)
type Entry struct{
	Best *model.Candidate \`json:"best,omitempty"\`
	ExpiresAt time.Time \`json:"expires_at"\`
}
type Store struct{
	mu sync.RWMutex
	path string
	max int
	entries map[string]Entry
	now func()time.Time
}
func New(path string,max int)*Store{
	s:=&Store{path:path,max:max,entries:make(map[string]Entry),now:time.Now}
	if bytes,err:=os.ReadFile(path);err==nil &&len(bytes)<16<<20 {
		_ = json.Unmarshal(bytes,&s.entries)
	}
	if s.entries==nil{s.entries=make(map[string]Entry)}
	return s
}
func Key(t model.Track)string{
	raw:=strings.ToLower(strings.TrimSpace(t.Artist))+"\n"+
		strings.ToLower(strings.TrimSpace(t.Title))+"\n"+
		strings.ToLower(strings.TrimSpace(t.Album))+"\n"+
		strings.TrimSpace(strings.ToLower(strings.TrimSpace(
			time.Duration(t.DurationSeconds*float64(time.Second)).String())))
	digest:=sha256.Sum256([]byte(raw))
	return hex.EncodeToString(digest[:])
}
func(s *Store)Get(t model.Track)(Entry,bool){
	s.mu.RLock();defer s.mu.RUnlock()
	v,ok:=s.entries[Key(t)]
	return v,ok&&s.now().Before(v.ExpiresAt)
}
func(s *Store)Put(t model.Track,best *model.Candidate,ttl time.Duration)error{
	s.mu.Lock();defer s.mu.Unlock()
	s.entries[Key(t)]=Entry{Best:best,ExpiresAt:s.now().Add(ttl)}
	if len(s.entries)>s.max {
		now:=s.now()
		for k,v:=range s.entries {if !now.Before(v.ExpiresAt){delete(s.entries,k)}}
		for len(s.entries)>s.max {
			var oldest string;var t time.Time
			for k,v:=range s.entries {if oldest==""||v.ExpiresAt.Before(t){oldest=k;t=v.ExpiresAt}}
			delete(s.entries,oldest)
		}
	}
	return s.flush()
}
func(s *Store)flush()error{
	if err:=os.MkdirAll(filepath.Dir(s.path),0700);err!=nil{return err}
	data,err:=json.Marshal(s.entries);if err!=nil{return err}
	temp,err:=os.CreateTemp(filepath.Dir(s.path),".lyrics-*.tmp")
	if err!=nil{return err}
	name:=temp.Name()
	defer os.Remove(name)
	if err=temp.Chmod(0600);err!=nil{temp.Close();return err}
	if _,err=temp.Write(data);err!=nil{temp.Close();return err}
	if err=temp.Sync();err!=nil{temp.Close();return err}
	if err=temp.Close();err!=nil{return err}
	return os.Rename(name,s.path)
}
