import sys, statistics
names=['Desert','Plains','Grassland','Forest','Hills','Mountains','Tundra','Arctic','Swamp','Jungle','Water','River']
for path in sys.argv[1:]:
    g=[[int(t.split(':')[0]) for t in l.split()] for l in open(path)]
    H,W=len(g),len(g[0]); seen=set(); regions={}
    for y in range(H):
        for x in range(W):
            if (x,y) in seen: continue
            t=g[y][x]; q=[(x,y)]; seen.add((x,y)); n=0
            while q:
                cx,cy=q.pop(); n+=1
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx,ny=(cx+dx)%W,cy+dy
                    if 0<=ny<H and (nx,ny) not in seen and g[ny][nx]==t: seen.add((nx,ny)); q.append((nx,ny))
            regions.setdefault(t,[]).append(n)
    print('==',path)
    land=sum(1 for r in g for t in r if t not in (10,))
    for t in range(12):
        if t==10 or t not in regions: continue
        rs=regions[t]; print(f'{names[t]:10s} tiles {sum(rs):4d}  regions {len(rs):3d}  mean {statistics.mean(rs):4.1f}  median {statistics.median(rs):3.0f}  single-tile {sum(1 for r in rs if r==1):3d} ({100*sum(1 for r in rs if r==1)/len(rs):.0f}%)')
    lat=lambda y: abs(y-24.5)/24.5
    arctic_off=sum(1 for y in range(H) for x in range(W) if g[y][x]==7 and 2<=y<=47)
    hot_high=sum(1 for y in range(H) for x in range(W) if g[y][x] in (0,9) and lat(y)>0.6)
    def nb(x,y): return [g[y+dy][(x+dx)%W] for dx in (-1,0,1) for dy in (-1,0,1) if (dx or dy) and 0<=y+dy<H]
    jd=sum(1 for y in range(H) for x in range(W) if g[y][x]==9 and any(n in (0,7) for n in nb(x,y)))
    mtn=regions.get(5,[])
    print(f'arctic away from the two polar rows: {arctic_off}; desert/jungle beyond 60% latitude: {hot_high}; jungle beside desert or arctic: {jd}')
