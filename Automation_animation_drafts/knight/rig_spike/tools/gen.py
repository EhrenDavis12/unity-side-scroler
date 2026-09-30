import urllib.error, sys, json, time, base64, urllib.request, os
tok=os.environ["REPLICATE_API_TOKEN"]
def call(instruction, images, out, ar="1:1", a={}):
    uris=[ "data:image/png;base64,"+base64.b64encode(open(p,"rb").read()).decode() for p in images]
    body={"input":{"instruction":instruction,"init_images":uris,"aspect_ratio":ar,"resolution":"1K","output_format":"png","transparency":a.get("transparent",False),"enhance_prompt":False}}
    req=urllib.request.Request("https://api.replicate.com/v1/models/sourceful/riverflow-2.0-pro/predictions",data=json.dumps(body).encode(),headers={"Authorization":"Bearer "+tok,"Content-Type":"application/json","Prefer":"wait=60"})
    for w in (0,20,40,80,160):
        time.sleep(w)
        try: p=json.load(urllib.request.urlopen(req)); break
        except urllib.error.HTTPError as e:
            if e.code!=429: raise
    else: print("GAVE UP"); return
    while p["status"] not in ("succeeded","failed","canceled"):
        time.sleep(3); p=json.load(urllib.request.urlopen(urllib.request.Request(p["urls"]["get"],headers={"Authorization":"Bearer "+tok})))
    if p["status"]!="succeeded": print("FAIL",p.get("error")); return
    url=p["output"] if isinstance(p["output"],str) else p["output"][0]
    urllib.request.urlretrieve(url,out); print("ok",out)
if __name__=="__main__":
    a=json.load(open(sys.argv[1])); call(a["instruction"],a["images"],a["out"],a.get("ar","1:1"),a)
