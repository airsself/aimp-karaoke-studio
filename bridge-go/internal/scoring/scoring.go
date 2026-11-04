package scoring

import (
	"math"
	"strings"
	"unicode"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)

// Normalize is deterministic and intentionally dependency-free. Decomposed
// diacritics are removed. No fuzzy guess can hide a different song title.
func Normalize(s string) string {
	var b strings.Builder
	for _,r:=range strings.ToLower(s) {
		if unicode.Is(unicode.Mn,r) {continue}
		if unicode.IsLetter(r)||unicode.IsNumber(r){b.WriteRune(r)}
	}
	return b.String()
}
func overlap(a,b string) bool{return a!=""&&b!=""&&(strings.Contains(a,b)||strings.Contains(b,a))}
func Score(request,found model.Track) float64 {
	a,b:=Normalize(request.Artist),Normalize(found.Artist)
	t,u:=Normalize(request.Title),Normalize(found.Title)
	if a==""||b==""||t==""||u=="" {return 0}
	artist,title:=0.0,0.0
	if a==b {artist=1} else if overlap(a,b) {artist=.78}
	if t==u {title=1} else if overlap(t,u) {title=.60}
	value:=artist*.38 + title*.62
	if found.DurationSeconds>0&&request.DurationSeconds>0&&
		math.Abs(found.DurationSeconds-request.DurationSeconds)>15 {value-=.25}
	return math.Max(0,math.Min(1,value))
}
