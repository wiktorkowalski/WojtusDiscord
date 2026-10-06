"""Checks for the four generated dashboards in ../grafana/dashboards/wojtusdiscord.

  validate.py            unique panel ids, layout inside 24 columns with no overlap, every panel
                         with a data source, a unit and a description
  validate.py --live     also sends every Prometheus query to the server (PROM_URL, default below)
  validate.py --live --data    and lists the panels whose query returns series now
"""
import json, os, sys, urllib.error, urllib.parse, urllib.request
OUT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','grafana','dashboards','wojtusdiscord'); live='--live' in sys.argv
PROM_URL=os.environ.get('PROM_URL','https://prometheus.home.vicio.ovh')
SUB={'$__rate_interval':'1m','$__interval_ms':'60000','$__interval':'1m','$__range':'6h','$event_type':'.*'}
def sub(e):
    for k,v in SUB.items(): e=e.replace(k,v)
    return e
def prom(e):
    u=PROM_URL+'/api/v1/query?'+urllib.parse.urlencode({'query':e})
    try:
        d=json.load(urllib.request.urlopen(u,timeout=60)); return d['status'],len(d['data']['result'])
    except urllib.error.HTTPError as x:
        return 'ERR '+x.read().decode()[:300],0
bad=0
for name in ['wojtus-overview','wojtus-events','wojtus-ai','wojtus-runtime']:
    d=json.load(open(f'{OUT}/{name}.json'))
    flat=[]
    for p in d['panels']:
        flat.append(p); flat+=p.get('panels',[])
    ids=[p['id'] for p in flat]
    assert len(ids)==len(set(ids)),name+' duplicate ids'
    assert d['schemaVersion']==41 and d['tags']==['wojtusdiscord'] and d['refresh']=='30s' and d['uid']==name
    occupied={}
    kinds={}
    for p in flat:
        g=p['gridPos']; kinds[p['type']]=kinds.get(p['type'],0)+1
        assert g['x']>=0 and g['w']>0 and g['x']+g['w']<=24, (name,p['title'],g)
        if p['type']=='row': continue
        if p['type']=='text':
            for x in range(g['x'],g['x']+g['w']):
                for y in range(g['y'],g['y']+g['h']): occupied[(x,y)]=p['title']
            continue
        for x in range(g['x'],g['x']+g['w']):
            for y in range(g['y'],g['y']+g['h']):
                assert (x,y) not in occupied,(name,p['title'],'overlaps',occupied[(x,y)])
                occupied[(x,y)]=p['title']
        assert isinstance(p.get('datasource'),dict) and p['datasource'].get('uid'),(name,p['title'],'datasource')
        if p['type']!='logs':
            assert p['fieldConfig']['defaults'].get('unit'),(name,p['title'],'unit')
        assert p.get('description'),(name,p['title'],'description')
        for t in p['targets']:
            assert isinstance(t.get('datasource'),dict)
            if live and t['datasource']['type']=='prometheus':
                st,n=prom(sub(t['expr']))
                if st!='success': bad+=1; print('  BAD',name,p['title'],st,'\n     ',sub(t['expr'])[:400])
                elif n and '--data' in sys.argv: print('  data',n,name,'|',p['title'])
    print(name,'ok: panels',len([p for p in flat if p['type']!='row']),'rows',kinds.get('row',0),kinds)
if live: print('bad queries',bad)
sys.exit(1 if bad else 0)
