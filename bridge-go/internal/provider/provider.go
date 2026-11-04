// Package provider contains independently replaceable online lyrics adapters.
// Providers do not own HTTP routing, scoring, caching or persistence.
package provider

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"time"

	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)

const maxBody = 1<<20
var ErrRateLimited = errors.New("provider rate limited")
type Provider interface {
	Name() string
	Search(ctx context.Context,track model.Track)([]model.Candidate,error)
}
type Client struct{HTTP *http.Client}
func NewClient(timeout time.Duration)*Client {
	return &Client{HTTP:&http.Client{Timeout:timeout}}
}
func (c *Client) Get(ctx context.Context,rawURL string)([]byte,error){
	req,err:=http.NewRequestWithContext(ctx,http.MethodGet,rawURL,nil)
	if err!=nil{return nil,err}
	req.Header.Set("User-Agent","AimpKaraokeStudio-GoBridge/4.1 (+https://github.com/airsself/aimp-karaoke-studio)")
	resp,err:=c.HTTP.Do(req)
	if err!=nil{return nil,err}
	defer resp.Body.Close()
	switch resp.StatusCode {
	case http.StatusNotFound:return nil,nil
	case http.StatusTooManyRequests:return nil,ErrRateLimited
	}
	if resp.StatusCode<200||resp.StatusCode>299{return nil,fmt.Errorf("provider HTTP %d",resp.StatusCode)}
	if resp.ContentLength>maxBody{return nil,errors.New("provider response exceeds limit")}
	data,err:=io.ReadAll(io.LimitReader(resp.Body,maxBody+1))
	if err!=nil{return nil,err}
	if len(data)>maxBody{return nil,errors.New("provider response exceeds limit")}
	return data,nil
}
