// Package lrc owns text classification, synchronized line timestamps,
// Enhanced-LRC word boundaries and optional [offset:...] normalization.
// It never replaces the input content, so precise original word timings survive.
package lrc

import (
	"bufio"
	"regexp"
	"strconv"
	"strings"
)

type Word struct {AtMS int `json:"at_ms"`; Text string `json:"text"`}
type Line struct {
	AtMS int `json:"at_ms"`
	Text string `json:"text"`
	Words []Word `json:"words,omitempty"`
}
type Document struct {
	Lines []Line `json:"lines"`
	Synced bool `json:"synced"`
	OffsetMS int `json:"offset_ms"`
}
var timeTag=regexp.MustCompile(`^\[(\d{1,3}):(\d{2})(?:[.](\d{1,3}))?\]`)
var wordTag=regexp.MustCompile(`<(\d{1,3}):(\d{2})(?:[.](\d{1,3}))?>`)
var offsetTag=regexp.MustCompile(`(?i)^\[offset:([+-]?\d+)\]$`)
func stamp(groups []string) (int,bool) {
	m,me:=strconv.Atoi(groups[1]); s,se:=strconv.Atoi(groups[2])
	if me!=nil||se!=nil||s>59{return 0,false}
	ms:=0
	if len(groups)>3&&groups[3]!=""{
		p:=groups[3]+strings.Repeat("0",3-len(groups[3]))
		ms,_=strconv.Atoi(p)
	}
	return (m*60+s)*1000+ms,true
}
func Parse(text string,maxBytes int) Document {
	var doc Document
	if len(text)>maxBytes||maxBytes<=0{return doc}
	reader:=bufio.NewScanner(strings.NewReader(strings.ReplaceAll(text,"\r\n","\n")))
	reader.Buffer(make([]byte,4096),maxBytes+1)
	for reader.Scan() {
		row:=strings.TrimSpace(strings.TrimPrefix(reader.Text(),"\ufeff"))
		if row==""{continue}
		if m:=offsetTag.FindStringSubmatch(row);m!=nil{
			v,err:=strconv.Atoi(m[1]);if err==nil&&v>=-600000&&v<=600000{doc.OffsetMS=v}
			continue
		}
		var stamps []int
		for {
			m:=timeTag.FindStringSubmatchIndex(row)
			if m==nil {break}
			raw:=timeTag.FindStringSubmatch(row)
			at,ok:=stamp(raw);if !ok {break}
			stamps=append(stamps,at);row=row[m[1]:]
			if len(stamps)>32 {break}
		}
		if len(stamps)>0 {
			doc.Synced=true
			parts:=wordTag.FindAllStringSubmatchIndex(row,-1)
			pure:=wordTag.ReplaceAllString(row,"")
			for _,at:=range stamps {
				line:=Line{AtMS:at+doc.OffsetMS,Text:strings.TrimSpace(pure)}
				if line.AtMS<0{line.AtMS=0}
				last,atWord:=0,0
				for _,loc:=range parts {
					if last>0 {
						part:=row[last:loc[0]]
						if part!="" {line.Words=append(line.Words,Word{AtMS:atWord+doc.OffsetMS,Text:part})}
					}
					raw:=wordTag.FindStringSubmatch(row[loc[0]:loc[1]])
					atWord,_=stamp(raw)
					last=loc[1]
				}
				if last>0&&last<len(row){line.Words=append(line.Words,Word{AtMS:atWord+doc.OffsetMS,Text:row[last:]})}
				doc.Lines=append(doc.Lines,line)
			}
		} else if !strings.HasPrefix(row,"[") {
			doc.Lines=append(doc.Lines,Line{Text:row})
		}
		if len(doc.Lines)>5000 {break}
	}
	return doc
}
func Synced(text string) bool{return Parse(text,1<<20).Synced}
