"""Collect public category metadata and representative images; never imports products.
Four bounded workers, resumable JSON cache, no credentials or bot/session cookies.
"""
import argparse, concurrent.futures, json, pathlib, re, threading, time, urllib.parse, urllib.request, urllib.error
API='https://common-api.rozetka.com.ua/v1/api/'

def collect(cache,workers):
    cache.mkdir(parents=True,exist_ok=True);stop=threading.Event()
    def request(url):
        for attempt in range(2):
            try:
                with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'TrebaCatalogImporter/1.0'}),timeout=20) as r:
                    raw=r.read(8*1024*1024+1)
                    if len(raw)>8*1024*1024:raise ValueError('Category response exceeded limit')
                    return json.loads(raw)
            except urllib.error.HTTPError as error:
                if error.code==429:stop.set();raise RuntimeError('Source rate limit reached; retry later') from error
                if error.code<500:raise
                if attempt:raise
                time.sleep(1)
    source=request(API+'categories/all?lang=ua&country=UA')
    if not isinstance(source.get('data'),list):raise ValueError('Invalid source taxonomy')
    (cache/'all-api.json').write_text(json.dumps(source,ensure_ascii=False),encoding='utf-8')
    nodes=[]
    def walk(items):
        for n in items:nodes.append(n);walk(n.get('children',[]))
    walk(source['data'])
    for sub in ('category-tiles','leaf-images'):(cache/sub).mkdir(exist_ok=True)
    def fetch(item):
        node,folder=item;path=cache/folder/(str(node['id'])+'.json')
        if path.exists():return True
        if stop.is_set():return False
        endpoint=API+'pages/catalog/category?'+urllib.parse.urlencode({'url':node['href'],'platform':'desktop','lang':'ua','country':'UA','filterOptionsLazy':'true','aspect_ratio':'rect'})
        try:
            d=request(endpoint).get('data',{});goods=d.get('goods',{}).get('tiles',[]) if d.get('category',{}).get('id')==node['id'] else []
            image=next((g.get('images',{}).get('main') or g.get('images',{}).get('preview') for g in goods if g.get('images',{}).get('main') or g.get('images',{}).get('preview')),None)
            if folder=='category-tiles':
                result={'id':node['id'],'representative':image,'tiles':[{'id':v.get('mobileUrl',{}).get('id',v.get('id')),'url':v.get('href'),'image':v.get('attach')} for v in d.get('categoryTilesBlock',{}).get('items',[]) if not v.get('custom') and v.get('attach')]}
            else:result={'id':node['id'],'image':image,'kind':'representative-product' if image else 'no-photo'}
            path.write_text(json.dumps(result,ensure_ascii=False),encoding='utf-8');return True
        except (urllib.error.URLError,TimeoutError,ValueError):return False
        finally:time.sleep(.35)
    def batch(items):
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
            for start in range(0,len(items),40):
                if stop.is_set():raise RuntimeError('Source requested a rate limit pause')
                outcomes=list(pool.map(fetch,items[start:start+40]));print(f'Collected {min(start+40,len(items))}/{len(items)}; cached or successful: {sum(outcomes)}',flush=True)
    batch([(n,'category-tiles') for n in nodes if n.get('children')])
    covered=set()
    for f in (cache/'category-tiles').glob('*.json'):
        d=json.loads(f.read_text(encoding='utf-8'))
        if d.get('representative'):covered.add(d['id'])
        for t in d['tiles']:
            if t['id'] and re.search('/c'+str(t['id'])+'/?(?:$|[?#])',t.get('url') or ''):covered.add(t['id'])
    batch([(n,'leaf-images') for n in nodes if n['id'] not in covered])

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--cache',type=pathlib.Path,required=True);p.add_argument('--workers',type=int,choices=range(1,5),default=4);args=p.parse_args();collect(args.cache,args.workers)
