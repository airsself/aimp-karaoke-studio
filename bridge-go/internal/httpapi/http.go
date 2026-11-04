// Package httpapi owns the backward-compatible WebLyrics marker endpoint
// and a new, documented JSON endpoint. No unknown binary-only APIs are copied.
package httpapi

import(
	"context"
	"encoding/json"
	"net"
	"net/http"
	"strconv"
	"strings"
	"time"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)
type Finder interface {
	Search(context.Context,model.Track,bool)(model.Result,error)
}
type API struct{ Finder Finder; MaxBytes int }
func New(finder Finder,max int)http.Handler{
	a:=&API{Finder:finder,MaxBytes:max}
	mux:=http.NewServeMux()
	mux.HandleFunc("GET /healthz",func(w http.ResponseWriter,r *http.Request){
		w.Header().Set("Content-Type","application/json")
		w.Write([]byte("{\"status\":\"ok\",\"engine\":\"go\",\"version\":\"4.1\"}"))
	})
	mux.HandleFunc("GET /lyrics",a.Legacy)
	mux.HandleFunc("GET /api/v1/search",a.JSON)
	return http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
		// Refuse DNS-rebinding access from non-loopback Host headers.
		host,_,err:=net.SplitHostPort(r.Host)
		if err!=nil{host=r.Host}
		if host!="127.0.0.1" && host!="localhost" && host!="::1" {
			http.Error(w,"loopback only",http.StatusForbidden);return
		}
		w.Header().Set("Cache-Control","no-store")
		mux.ServeHTTP(w,r)
	})
}
func(a *API) input(r *http.Request)(model.Track,bool,bool){
	q:=r.URL.Query()
	t:=model.Track{Artist:strings.TrimSpace(q.Get("artist")),
		Title:strings.TrimSpace(q.Get("title")),Album:strings.TrimSpace(q.Get("album"))}
	if val:=q.Get("duration");val!="" {
		if sec,err:=strconv.ParseFloat(val,64);err==nil && sec>=0&&sec<=86400 {
			t.DurationSeconds=sec
		}
	}
	force:=q.Get("force")=="1"||q.Get("nocache")=="1"
	return t,force,t.Valid()
}
func(a *API) Legacy(w http.ResponseWriter,r *http.Request){
	t,force,valid:=a.input(r)
	w.Header().Set("Content-Type","text/plain; charset=utf-8")
	if !valid {
		w.WriteHeader(http.StatusBadRequest)
		w.Write([]byte("AIMPLB-STATUS: MISSING_METADATA"))
		return
	}
	result,err:=a.Finder.Search(r.Context(),t,force)
	if err!=nil {
		w.WriteHeader(http.StatusServiceUnavailable)
		w.Write([]byte("AIMPLB-STATUS: PROVIDER_ERROR"))
		return
	}
	if result.Best==nil||len(result.Best.Content)>a.MaxBytes {
		w.Write([]byte("AIMPLB-STATUS: NOT_FOUND"));return
	}
	c:=result.Best
	content:=strings.NewReplacer("\r\n","\n","\r","\n").Replace(c.Content)
	if strings.Contains(content,"AIMPLB-LYRICS-END")||
		strings.Contains(content,"AIMPLB-LINEBREAK-7F3C") {
		w.WriteHeader(http.StatusUnprocessableEntity)
		w.Write([]byte("AIMPLB-STATUS: INVALID_LYRICS"));return
	}
	encode:=func(text string)string{return strings.ReplaceAll(text,"\n","AIMPLB-LINEBREAK-7F3C")}
	// The old WebLyrics ini extracts all four marker pairs.
	body:="AIMPLB-STATUS: OK\n"+
		"AIMPLB-ARTIST-BEGIN"+encode(c.Track.Artist)+"AIMPLB-ARTIST-END\n"+
		"AIMPLB-TITLE-BEGIN"+encode(c.Track.Title)+"AIMPLB-TITLE-END\n"+
		"AIMPLB-ALBUM-BEGIN"+encode(c.Track.Album)+"AIMPLB-ALBUM-END\n"+
		"AIMPLB-LYRICS-BEGIN"+encode(content)+"AIMPLB-LYRICS-END"
	w.Write([]byte(body))
}
func(a *API) JSON(w http.ResponseWriter,r *http.Request){
	t,force,valid:=a.input(r)
	if !valid{http.Error(w,"artist/title required",http.StatusBadRequest);return}
	result,err:=a.Finder.Search(r.Context(),t,force)
	if err!=nil{http.Error(w,err.Error(),http.StatusServiceUnavailable);return}
	w.Header().Set("Content-Type","application/json; charset=utf-8")
	encoder:=json.NewEncoder(w);encoder.SetEscapeHTML(true)
	_ = encoder.Encode(result)
}
// NewServer configures bounded client reads and slow-client timeouts.
func NewServer(listener net.Listener,handler http.Handler)*http.Server{
	return &http.Server{Handler:handler,
		ReadHeaderTimeout:5*time.Second,ReadTimeout:15*time.Second,
		WriteTimeout:45*time.Second,IdleTimeout:30*time.Second,
		MaxHeaderBytes:8192}
}
