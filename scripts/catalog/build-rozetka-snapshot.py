"""Build a reviewed, additive Treba category snapshot from Rozetka's public taxonomy.
No products or database rows are written. Input files come from public category APIs.
Usage: py scripts/catalog/build-rozetka-snapshot.py --cache .treba-local/rozetka-catalog --treba-tree .treba-local/published-category-tree.json --output src/Rozetka-clone/WebApi/CatalogData/rozetka-categories.json
"""
import argparse, collections, datetime, hashlib, json, pathlib, re, unicodedata, urllib.parse

def normalized(value):
    return ''.join(c for c in unicodedata.normalize('NFKC', value).casefold() if c.isalnum())

ROOT_BINDINGS = {80253:'laptops',80025:'appliances',4625734:'books',1162030:'clothes',2394287:'home',4629305:'beauty',2577232:'tools',4628418:'plumbing',2394297:'garden',88468:'kids',4626923:'drinks',4627851:'business',4627858:'tools-avtotovary',4627949:'electronics',4627893:'sport',80261:'gaming',3520929:'pets'}
ALIASES = {"Комп'ютерні комплектуючі":'Комплектуючі','Офісна техніка':'Оргтехніка','Програмне забезпечення':'Софт та ліцензії',"Комп'ютерні миші":'Миші','Маршрутизатори':'Роутери','Адаптери Powerline':'Powerline адаптери','Системи охолодження':'Кулери та охолодження','Приготування їжі':'Для приготування їжі','Мобільні телефони':'Смартфони'}

def build(cache, treba_tree, output):
    tree=json.loads((cache/'all-api.json').read_text(encoding='utf-8-sig'))['data']
    current=json.loads(treba_tree.read_text(encoding='utf-8-sig')); existing=[]
    def flatten(nodes,parent=None,root=None):
        for n in nodes:
            n=dict(n);n['_parent']=parent;n['_root']=root or n['id'];existing.append(n)
            flatten(n['children'],n['id'],root or n['id'])
    flatten(current);by_slug={n['slug']:n for n in existing};by_id={n['id']:n for n in existing}
    photos={};kinds={}
    for f in sorted((cache/'portals').glob('*.json')):
        def portal(v):
            if isinstance(v,dict):
                url=v.get('link',v.get('href',''));m=re.search(r'/c([0-9]+)/?(?:$|[?#])',url) if isinstance(url,str) else None
                if m and isinstance(v.get('preview_image'),str):photos.setdefault(m[1],v['preview_image']);kinds.setdefault(m[1],'category')
                for x in v.values():portal(x)
            elif isinstance(v,list):
                for x in v:portal(x)
        portal(json.loads(f.read_text(encoding='utf-8')))
    for f in sorted((cache/'category-tiles').glob('*.json')):
        d=json.loads(f.read_text(encoding='utf-8'))
        for t in d['tiles']:
            if t['id'] and re.search('/c'+str(t['id'])+'/?(?:$|[?#])',t.get('url') or ''):
                photos.setdefault(str(t['id']),t['image']);kinds.setdefault(str(t['id']),'category')
        if d.get('representative'):photos.setdefault(str(d['id']),d['representative']);kinds.setdefault(str(d['id']),'representative-product')
    for f in sorted((cache/'leaf-images').glob('*.json')):
        d=json.loads(f.read_text(encoding='utf-8'))
        if d.get('image'):photos.setdefault(str(d['id']),d['image']);kinds.setdefault(str(d['id']),'representative-product')
    for key,url in list(photos.items()):
        parsed=urllib.parse.urlparse(url)
        if parsed.scheme!='https' or not (parsed.hostname=='rozetka.com.ua' or (parsed.hostname or '').endswith('.rozetka.com.ua')):
            del photos[key];kinds.pop(key,None)
        else:photos[key]=urllib.parse.urlunparse(parsed._replace(fragment=''))
    used=set();result=[];targets={}
    def visit(nodes,parent_key=None,target_parent=None,target_root=None):
        for order,n in enumerate(nodes):
            key='c'+str(n['id']);target=by_slug.get(ROOT_BINDINGS.get(n['id'],''))
            if target is None:
                names={normalized(n['title']),normalized(ALIASES.get(n['title'],n['title']))}
                candidates=[c for c in existing if c['id'] not in used and normalized(c['name']) in names and (c['_root']==target_root or c['_parent']==target_parent)]
                siblings=[c for c in candidates if c['_parent']==target_parent]
                if len(siblings)==1:target=siblings[0]
                elif len(candidates)==1:target=candidates[0]
            if target and target['id'] in used:target=None
            if target:used.add(target['id'])
            image=photos.get(str(n['id']))
            record={'key':key,'parentKey':parent_key,'name':n['title'],'sourceUrl':n['href'],'sortOrder':order,'existingSlug':target['slug'] if target else None,'existingName':target['name'] if target else None,'imageUrl':image,'imageKind':kinds.get(str(n['id']))}
            if len(record['name'])>200:raise ValueError('Source category name exceeds schema: '+key)
            result.append(record);targets[key]=target
            visit(n.get('children',[]),key,target['id'] if target else None,target['_root'] if target else target_root)
    visit(tree)
    by_key={r['key']:r for r in result}
    # Source categories with no inventory may have no photo; use a documented nearby category photo.
    children=collections.defaultdict(list)
    for r in result:children[r['parentKey']].append(r)
    for r in reversed(result):
        if not r['imageUrl']:
            photo=next((c['imageUrl'] for c in children[r['key']] if c['imageUrl']),None)
            if photo:r['imageUrl']=photo;r['imageKind']='descendant-category'
    for r in result:
        if not r['imageUrl'] and r['parentKey'] and by_key[r['parentKey']]['imageUrl']:
            r['imageUrl']=by_key[r['parentKey']]['imageUrl'];r['imageKind']='parent-category'
    output.parent.mkdir(parents=True,exist_ok=True)
    snapshot={'version':'2026-10-21','source':'https://rozetka.com.ua/ua/all-categories-goods/','categories':result}
    text='{"version":"2026-10-21","source":'+json.dumps(snapshot['source'])+',"categories":[\n'+',\n'.join(json.dumps(r,ensure_ascii=False,separators=(',',':')) for r in result)+'\n]}\n'
    output.write_text(text,encoding='utf-8')
    report={'sourceCategories':len(result),'existingCategories':len(existing),'matchedExisting':len(used),'newCategories':len(result)-len(used),'expectedTotal':len(existing)+len(result)-len(used),'sourceOrRepresentativeImages':sum(k in ('category','representative-product') for k in (r['imageKind'] for r in result)),'inheritedImages':sum(k in ('descendant-category','parent-category') for k in (r['imageKind'] for r in result)),'missingImages':[r['key'] for r in result if not r['imageUrl']],'snapshotSha256':hashlib.sha256(text.encode()).hexdigest()}
    (cache/'comparison-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--cache',type=pathlib.Path,required=True);p.add_argument('--treba-tree',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args();build(a.cache,a.treba_tree,a.output)
