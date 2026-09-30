"""Focused V3 ground correction. Source V2, anchors, buildings and foliage are immutable.

Run on V2: -- OUTPUT_DIRECTORY sample
Run on the staged V3: -- OUTPUT_DIRECTORY promote
No Unity asset writes occur. Promotion requires an explicit ground-review gate file.
"""
import bpy
import bmesh
import hashlib
import json
import math
import random
import shutil
import sys
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt
from mathutils.bvhtree import BVHTree

args=sys.argv[sys.argv.index('--')+1:]
out=Path(args[0]).resolve();out.mkdir(parents=True,exist_ok=True)
mode=args[1] if len(args)>1 else 'sample'
loaded_path=Path(bpy.data.filepath)
scene=bpy.data.scenes['V2 Environment Review'];bpy.context.window.scene=scene
bpy.context.view_layer.update()
island=scene.objects['V2 Island continuous fractured bedrock']
old_island=island.data
source_sha=hashlib.sha256(loaded_path.read_bytes()).hexdigest()
if mode=='sample':
    if loaded_path.name!='MapScene_Environment_Ruins_v2.blend':raise RuntimeError('Sample must start from untouched native V2.')
    backup=out/'MapScene_Environment_Ruins_v2_before_ground.blend'
    if not backup.exists():shutil.copy2(loaded_path,backup)
    if hashlib.sha256(backup.read_bytes()).hexdigest()!=source_sha:raise RuntimeError('Existing backup differs from source V2.')
elif mode=='promote':
    gate=out/'ground-gate.json'
    if not gate.exists() or not json.loads(gate.read_text())['allow_full_pavement']:
        raise RuntimeError('Full pavement requires a recorded ground sample gate.')
    sample_backup=out/'MapScene_Environment_Ruins_v3_sample.blend'
    if not sample_backup.exists():shutil.copy2(loaded_path,sample_backup)
else:raise RuntimeError('Expected sample or promote mode.')
names={o.get('canonical_name',o.name):o for o in scene.objects}
anchor_snapshot={n:[list(row) for row in o.matrix_world] for n,o in names.items() if n.startswith('ANCHOR_Node_')}
portal_snapshot=[list(row) for row in names['Portal - carved solid ring'].matrix_world]
protected={o.name:{'matrix':[list(row) for row in o.matrix_world],
                  'vertices':len(o.data.vertices) if o.type=='MESH' else 0,
                  'faces':len(o.data.polygons) if o.type=='MESH' else 0}
           for o in scene.objects if not ('Island continuous' in o.name or 'Node terrace' in o.name or 'Paved route' in o.name)}
terraces=[names[f'Node terrace {i:02d}'].matrix_world.translation.copy() for i in range(13)]
centers=terraces+[Vector((14,0,1.25))]
routes=[(0,1),(0,2),(0,3),(1,4),(2,5),(3,6),(2,4),(3,5),(4,7),(5,8),(6,9),(4,8),(5,9),(7,10),(8,11),(9,12),(10,13),(11,13),(12,13)]
tree=BVHTree.FromObject(island,bpy.context.evaluated_depsgraph_get())


def smoothstep(a,b,x):
    t=max(0.0,min(1.0,(x-a)/(b-a)));return t*t*(3-2*t)


def old_height(x,y):
    hit,_,_,_=tree.ray_cast(Vector((x,y,20)),Vector((0,0,-1)),45)
    if hit is not None:return hit.z
    hit,_,_,_=tree.find_nearest(Vector((x,y,0)))
    return max(-.45,hit.z) if hit is not None else -.10


def route_sample(x,y):
    best=(1e6,0,0,0)
    for index,(ia,ib) in enumerate(routes):
        a,b=centers[ia],centers[ib];delta=Vector((b.x-a.x,b.y-a.y));p=Vector((x-a.x,y-a.y))
        t=max(0,min(1,p.dot(delta)/delta.length_squared));offset=p-delta*t
        if offset.length<best[0]:best=(offset.length,index,t,delta.x*offset.y-delta.y*offset.x)
    return best


root_seats=[];architecture_seats=[]
for obj in scene.objects:
    if obj.type!='MESH':continue
    if ('continuous trunk' in obj.name or 'rooted frond axes' in obj.name or obj.name.endswith('connected petioles')):
        count=12 if 'continuous trunk' in obj.name else 6
        point=sum((obj.matrix_world @ v.co for v in obj.data.vertices[:count]),Vector())/count
        root_seats.append((point.x,point.y,.20,.55))
    if any(c.name.startswith('V2 Environment architecture') for c in obj.users_collection):
        if 'fallen' in obj.name:continue
        points=[obj.matrix_world @ v.co for v in obj.data.vertices]
        minimum=min(p.z for p in points)
        low=[p for p in points if p.z<minimum+.16]
        if low:architecture_seats.append((min(p.x for p in low),max(p.x for p in low),min(p.y for p in low),max(p.y for p in low)))


def seat_lock(x,y):
    weight=0.0
    for px,py,inner,outer in root_seats:weight=max(weight,1-smoothstep(inner,outer,math.hypot(x-px,y-py)))
    for x0,x1,y0,y1 in architecture_seats:
        d=math.hypot(max(x0-x,0,x-x1),max(y0-y,0,y-y1))
        weight=max(weight,1-smoothstep(.05,.42,d))
    return weight


def ground_height(x,y):
    old=old_height(x,y)
    smoothed=(old*2+old_height(x-.32,y)+old_height(x+.32,y)+old_height(x,y-.32)+old_height(x,y+.32))/6
    relief=0.0
    for px,py,sx,sy,value in [(-13.5,3.7,2.0,1.6,.26),(-7.3,3.0,1.65,1.05,.35),(-4.6,-2.7,2.1,1.15,.23),
                               (1.3,3.0,1.9,1.15,.18),(5.8,-2.65,1.8,1.05,.33),(10.6,2.75,1.5,1.1,.27)]:
        relief+=value*math.exp(-(((x-px)/sx)**2+((y-py)/sy)**2))
    relief-=.08*math.exp(-((y-1.9-.30*math.sin(x*.28))/.58)**2)
    distance,index,t,side=route_sample(x,y)
    node_distance=min(math.hypot(x-p.x,y-p.y) for p in terraces)
    clear=max(1-smoothstep(.39,.75,distance),1-smoothstep(1.05,1.37,node_distance),seat_lock(x,y))
    z=smoothed+relief
    # Low banks cover selected slab edges but never the readable route center.
    bank=.145*math.exp(-((distance-.68)/.20)**2)*(.52+.48*math.sin(index*1.71+t*3.5+(0 if side>0 else 2.1))**2)
    bank*=smoothstep(1.25,1.85,node_distance)
    z+=bank
    for i,p in enumerate(terraces):
        d=math.hypot(x-p.x,y-p.y)
        if 1.2<d<2.25:
            direction=.45+.55*math.cos(math.atan2(y-p.y,x-p.x)-i*.81)**2
            z+=.16*math.exp(-((d-1.58)/.30)**2)*direction
    return z*(1-clear)+old*clear


def make_mesh(name,vertices,faces,materials):
    data=bpy.data.meshes.new(name);data.from_pydata(vertices,[],faces);data.update()
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
    for material in materials:data.materials.append(material)
    return data


def island_mesh():
    controls=list(reversed([(-18,-3),(-18,2),(-17,5.5),(-13,8.3),(-10.5,10),(-6.2,9.6),
        (-2,10.5),(2.5,9.4),(6,10),(10.8,9.3),(13,7.5),(16.1,6.1),(17.6,3.9),(18.6,.8),
        (18,-3.3),(16.2,-6),(12.4,-7.5),(8.7,-8.9),(4.5,-10.2),(0,-9.8),(-4.5,-9.5),(-8.4,-8.3),(-11.6,-8.9),(-14.8,-6.6)]))
    controls=[Vector(p) for p in controls]
    rim=[];planar_rim=[]
    for i in range(len(controls)):
        p0,p1,p2,p3=[controls[j%len(controls)] for j in (i-1,i,i+1,i+2)]
        for j in range(8):
            t=j/8
            p=.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)
            a=math.atan2(p.y/10,p.x/18)
            outward=p.normalized()
            p+=outward*(.10+.075*math.sin(a*5+.7)+.06*math.cos(a*9-1.2))
            rim.append(p)
            planar_rim.append(p1.lerp(p2,t)+outward*.10)
    n=len(rim);vertices=[];faces=[];heights=[ground_height(p.x,p.y) for p in rim]
    # The shoulder uses close support profiles; lower rock masses do not repeat these contours.
    profiles=[(1,0), (1.006,-.16),(1.012,-.40),(1.007,-.75),(.985,-1.35),
              (.943,-2.25),(.914,-3.55),(.825,-4.85),(.705,-6.25),(.61,-7.0)]
    for level,(scale,depth) in enumerate(profiles):
        t=level/(len(profiles)-1)
        for i,p in enumerate(rim):
            a=math.atan2(p.y/10,p.x/18)
            # Three broad oblique ridges and quieter secondary breaks, not random facet noise.
            mass=.35*math.sin(a*3+1.1+t*1.8)+.19*math.sin(a*5-.4-t*2.2)
            weight=math.sin(t*math.pi)
            outward=p.normalized()
            plane_blend=.90*smoothstep(.24,.50,t)
            rock_outline=p.lerp(planar_rim[i],plane_blend)
            xy=rock_outline*scale+outward*mass*weight
            # A few asymmetric rock clefts cross the local depth profiles, carving one solid.
            # Their spacing, width, angle and depth differ; no repeated brick or contour bands.
            incision=0.0
            for center,width,amount,lean in [(-2.73,.105,.66,.11),(-2.17,.15,.48,-.13),
                                              (-1.82,.10,.92,.09),(-1.29,.15,.64,-.07),
                                              (-.72,.095,.82,.16),(-.10,.12,.56,-.12),
                                              (.69,.15,.74,.08),(1.21,.10,.54,-.14),(2.24,.14,.68,.12)]:
                angle=center+lean*(t-.25)
                gap=abs(math.atan2(math.sin(a-angle),math.cos(a-angle)))
                incision+=amount*max(0,1-gap/width)**1.45
            incidence=smoothstep(.20,.46,t)*(1-.72*smoothstep(.72,1,t))
            xy-=outward*incision*incidence*1.2
            if level>=4:xy+=Vector((.30*t*math.sin(a*2-.5),.21*t*math.cos(a*3)))
            z=heights[i]+depth+weight*(.50*math.sin(a*3+t*2.7)+.20*math.cos(a*7-t))
            vertices.append((xy.x,xy.y,z))
    for level in range(len(profiles)-1):
        for i in range(n):faces.append((level*n+i,level*n+(i+1)%n,(level+1)*n+(i+1)%n,(level+1)*n+i))
    side_count=len(faces)
    boundary=[Vector((p.x,p.y)) for p in rim];points=boundary[:]
    rng=random.Random(30930)
    for ix in range(-44,45):
        for iy in range(-26,27):
            x=(ix+rng.uniform(-.18,.18))*.43;y=(iy+rng.uniform(-.18,.18))*.43
            if (x/18.2)**2+(y/10.2)**2<.98:points.append(Vector((x,y)))
    # Explicit samples keep protected gameplay centers represented in the continuous surface.
    for p in terraces:
        points.append(Vector((p.x,p.y)))
        for radius in [1.1,1.42,1.65,1.90]:
            for j in range(32):points.append(Vector((p.x+radius*math.cos(j*math.tau/32),p.y+radius*math.sin(j*math.tau/32))))
    for ia,ib in routes:
        a,b=centers[ia],centers[ib]
        direction=Vector((b.x-a.x,b.y-a.y)).normalized();side=Vector((-direction.y,direction.x))
        count=max(6,round(math.hypot(b.x-a.x,b.y-a.y)/.36))
        for j in range(1,count):
            center=Vector((a.x+(b.x-a.x)*j/count,a.y+(b.y-a.y)*j/count))
            for offset in [-1.02,-.83,-.65,-.43,0,.43,.65,.83,1.02]:points.append(center+side*offset)
    top,_,triangles,_,_,_=delaunay_2d_cdt(points,[(i,(i+1)%n) for i in range(n)],[list(range(n))],1,1e-7)
    indices=[]
    for p in top:
        original=next((j for j,q in enumerate(boundary) if (p-q).length<1e-6),None)
        if original is not None:indices.append(original)
        else:indices.append(len(vertices));vertices.append((p.x,p.y,ground_height(p.x,p.y)))
    for tri in triangles:faces.append(tuple(indices[i] for i in tri))
    center=len(vertices);vertices.append((.2,0,-7.2))
    for i in range(n):faces.append((center,(len(profiles)-1)*n+i,(len(profiles)-1)*n+(i+1)%n))
    data=make_mesh('V3 Continuous weathered island geometry',vertices,faces,list(old_island.materials))
    for face in data.polygons:
        face.use_smooth=True
        face.material_index=0 if face.index<side_count or face.index>=side_count+len(triangles) else 1
    colors=data.color_attributes.new(name='COLOR_0',type='FLOAT_COLOR',domain='POINT')
    data.color_attributes.active_color_index=0;data.color_attributes.render_color_index=0
    for index,vertex in enumerate(data.vertices):
        x,y,z=vertex.co
        plant=max((math.exp(-((x-px)**2+(y-py)**2)/(1.35+((j%4)*.28))**2) for j,(px,py,_,_) in enumerate(root_seats)),default=0)
        damp=.5+.5*math.sin(x*.31+y*.23+1)*math.cos(y*.38-.4)
        rock_weight=smoothstep(-.15,-2.3,z) if z<-.15 else .07+.17*(.5+.5*math.sin(x*.29-y*.31))
        moss_weight=(.08+.60*plant+.10*damp)*(1-.86*rock_weight)
        if index<n*len(profiles):moss_weight*=1.0-smoothstep(.15,.6,index//n/(len(profiles)-1))
        distance,_,_,_=route_sample(x,y)
        moss_weight*=.48+.52*smoothstep(.55,1.2,distance)
        soil_weight=max(.03,1-rock_weight-moss_weight)
        total=soil_weight+moss_weight+rock_weight
        colors.data[index].color=(soil_weight/total,moss_weight/total,rock_weight/total,1)
    island.data=data;island['ground_revision']='V3 continuous weathered shoulder and broad rock massing'
    island['ground_blend_contract']='Linear COLOR_0: R soil, G moss, B exposed rock; normalized; alpha one'
    return side_count


def rounded_outline(points,width=.08):
    result=[]
    for i,p in enumerate(points):
        prev=points[i-1];following=points[(i+1)%len(points)]
        radius=min(width,(p-prev).length*.24,(following-p).length*.24)
        start=p+(prev-p).normalized()*radius;end=p+(following-p).normalized()*radius
        for k in range(4):
            t=k/3;result.append((1-t)**2*start+2*(1-t)*t*p+t*t*end)
    return result


def slab(vertices,faces,outline,height,seed):
    rng=random.Random(seed);outline=rounded_outline([Vector(p) for p in outline],.065+rng.random()*.045)
    center=sum(outline,Vector((0,0)))/len(outline);n=len(outline);offset=len(vertices)
    for scale,dz in [(1.0,-.15),(1.018,-.07),(1.0,-.023),(.965,0)]:
        for i,p in enumerate(outline):
            q=center+(p-center)*scale
            # Sparse shallow edge wear leaves broad top stone planes, not serrated noise.
            wear=.009*(.5+.5*math.sin(i*.35+seed))*max(0,(scale-.96)/.06)
            vertices.append((q.x,q.y,height+dz-wear))
    for ring in range(3):
        for i in range(n):faces.append((offset+ring*n+i,offset+ring*n+(i+1)%n,offset+(ring+1)*n+(i+1)%n,offset+(ring+1)*n+i))
    faces.append(tuple(offset+i for i in reversed(range(n))))
    faces.append(tuple(offset+3*n+i for i in range(n)))


def refine_court(index):
    obj=names[f'Node terrace {index:02d}']
    if obj.get('ground_revision')=='V3':return
    vertices=[];faces=[]
    tops=[p for p in obj.data.polygons if p.normal.z>.98]
    for i,poly in enumerate(tops):
        points=[obj.data.vertices[v].co for v in poly.vertices]
        slab(vertices,faces,[(p.x,p.y) for p in points],sum(p.z for p in points)/len(points),index*12+i)
    data=make_mesh(f'V3 Worn court {index:02d} geometry',vertices,faces,list(obj.data.materials))
    for p in data.polygons:p.use_smooth=len(p.vertices)==4
    obj.data=data;obj.modifiers.clear();obj['ground_revision']='V3'


def refine_road(index):
    obj=names[f'Paved route {index:02d}']
    if obj.get('ground_revision')=='V3':return
    a,b=[centers[i] for i in routes[index]];delta=b-a;delta.z=0;direction=delta.normalized();side=Vector((-direction.y,direction.x,0))
    start=a+direction*1.5;length=max(.8,delta.length-3)
    count=max(2,round(length/1.15));weights=[1+.19*math.sin(index*1.1+i*2.4) for i in range(count)]
    total=sum(weights);position=0;vertices=[];faces=[]
    old_tree=BVHTree.FromObject(obj,bpy.context.evaluated_depsgraph_get())
    for i,weight in enumerate(weights):
        u0=position+.02;u1=position+length*weight/total-.02;position+=length*weight/total
        split=.13*math.sin(i*1.3+index)
        spans=[(-.62,.62)] if (i+index)%3==0 else [(-.62,split-.018),(split+.018,.62)]
        for row,(v0,v1) in enumerate(spans):
            corners=[(u0+.02,v0),(u1-.04,v0+.012),(u1,v0+.09),(u1-.02,v1-.012),(u0+.025,v1),(u0,v1-.07)]
            outline=[(start+direction*u+side*v) for u,v in corners]
            middle=start+direction*((u0+u1)*.5)+side*((v0+v1)*.5)
            hit,_,_,_=old_tree.ray_cast(Vector((middle.x,middle.y,10)),Vector((0,0,-1)),20)
            height=hit.z if hit is not None else .10
            slab(vertices,faces,[(p.x,p.y) for p in outline],height,300+index*8+i*2+row)
    data=make_mesh(f'V3 Worn route {index:02d} geometry',vertices,faces,list(obj.data.materials))
    for p in data.polygons:p.use_smooth=len(p.vertices)==4
    obj.data=data;obj.modifiers.clear();obj['ground_revision']='V3'


def flatten_cliff_masses():
    mesh=island.data;n=192
    if len(mesh.vertices)<1920:raise RuntimeError('Expected authored ten-profile V3 cliff.')
    # Each main mass lies on a deliberate plane through a distant common apex.
    # Irregular sector widths define rock fracture size; only the narrow joints are softened.
    sectors=[0,13,29,47,60,79,96,111,132,146,163,178,192]
    apex=Vector((.4,-.3,-23))
    upper=[mesh.vertices[4*n+(i%n)].co.copy() for i in sectors]
    lower=[]
    for j,i in enumerate(sectors):
        target=mesh.vertices[8*n+(i%n)].co.z
        t=(target-upper[j].z)/(apex.z-upper[j].z)
        lower.append(upper[j].lerp(apex,t))
    profiles={4:0.0,5:.20,6:.43,7:.70,8:1.0,9:1.13}
    for sector in range(len(sectors)-1):
        start,end=sectors[sector],sectors[sector+1]
        for i in range(start,end):
            u=(i-start)/(end-start)
            top=upper[sector].lerp(upper[sector+1],u)
            bottom=lower[sector].lerp(lower[sector+1],u)
            side=Vector((top.x,top.y,0)).normalized()
            for level,t in profiles.items():
                point=top.lerp(bottom,t)
                border=max(0,1-min(u,1-u)/.12)
                notch=.16*border*math.sin(min(t,1)*math.pi)
                point-=side*notch
                mesh.vertices[level*n+i].co=point
    for face in mesh.polygons:
        if face.index>=9*n:continue
        level=face.index//n;i=face.index%n
        distance=min(min(abs(i-s),n-abs(i-s)) for s in sectors)
        face.use_smooth=level<4 or distance<2
    mesh.update()
    island['cliff_modeling']='Twelve irregular oblique fracture planes with narrow weathered joints and rounded upper shoulder'


if mode=='sample':
    island_mesh()
    for i in [5,6]:refine_court(i)
    for i in [5,9]:refine_road(i)
else:
    flatten_cliff_masses()
    for i in range(13):refine_court(i)
    for i in range(19):refine_road(i)

bpy.context.view_layer.update()
for name,matrix in anchor_snapshot.items():
    if [list(row) for row in names[name].matrix_world]!=matrix:raise RuntimeError('Anchor drift: '+name)
if [list(row) for row in names['Portal - carved solid ring'].matrix_world]!=portal_snapshot:raise RuntimeError('Portal drift')
for name,before in protected.items():
    obj=scene.objects[name]
    after={'matrix':[list(row) for row in obj.matrix_world],
           'vertices':len(obj.data.vertices) if obj.type=='MESH' else 0,
           'faces':len(obj.data.polygons) if obj.type=='MESH' else 0}
    if before!=after:raise RuntimeError('Protected object changed: '+name)
checks=[]
for obj in scene.objects:
    if obj.type!='MESH' or not obj.get('ground_revision'):continue
    bm=bmesh.new();bm.from_mesh(obj.data)
    bad=sum(f.calc_area()<1e-10 for f in bm.faces);open_edges=sum(not e.is_manifold for e in bm.edges)
    bm.free();obj.data.calc_loop_triangles()
    checks.append({'name':obj.name,'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),'degenerateFaces':bad,'nonManifoldEdges':open_edges})
    if bad or open_edges:raise RuntimeError('Ground topology check failed: '+obj.name)
report={'stage':mode,'sourceNative':str(loaded_path),'sourceSha256':source_sha,'originalAnchorsUnchanged':True,
        'allAnchorCount':len(anchor_snapshot),'portalUnchanged':True,'protectedObjectsUnchanged':len(protected),
        'terrainBlend':'COLOR_0 linear normalized RGB soil moss rock','meshes':checks,'visualAcceptance':'Pending same-camera clay gate'}
ground_materials={'V2 Cliff grey umber','V2 Warm silt','V2 Rooted moss'}
mask_checks=[]
for obj in scene.objects:
    if obj.type!='MESH' or not any(m and m.name in ground_materials for m in obj.data.materials):continue
    color=obj.data.color_attributes.active_color
    if color is None:raise RuntimeError('Ground material requires normalized vertex weights: '+obj.name)
    error=max(abs(sum(item.color[:3])-1) for item in color.data)
    if error>1e-5:raise RuntimeError('Ground blend weights are not normalized: '+obj.name)
    mask_checks.append({'object':obj.name,'layer':color.name,'sumError':error})
report['groundMaskChecks']=mask_checks
destination=out/'MapScene_Environment_Ruins_v3.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(destination))
(out/f'ground-{mode}-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

# Stage evidence uses existing neutral rig and precisely preserved game-axis camera.
background=next(c for c in scene.collection.children if c.name.startswith('Review Background'));background.hide_render=True
clay=bpy.data.materials.get('V2 Neutral clay')
if clay is None:
    clay=bpy.data.materials.new('V3 Neutral clay');clay.use_nodes=True
    clay.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.44,.44,.44,1)
    clay.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.86
scene.view_layers[0].material_override=clay
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.cycles.samples=24
def render(name,camera):
    scene.camera=camera;scene.render.filepath=str(out/name);bpy.ops.render.render(write_still=True,scene=scene.name)
render(f'01_Ground_{mode}_GameAxis_Clay.png',scene.objects['V2 Fixed game axis check'])
render(f'02_Ground_{mode}_Oblique_Clay.png',scene.objects['V2 Full game direction'])
rig=next(c for c in scene.collection.children if c.name.startswith('V2 Environment review rig'))
data=bpy.data.cameras.new('V3 Eroded shoulder inspection');data.lens=48
cam=bpy.data.objects.new('V3 Eroded shoulder inspection',data);rig.objects.link(cam)
cam.location=(2,-22,5);cam.rotation_euler=(Vector((0,-3,-.8))-cam.location).to_track_quat('-Z','Y').to_euler()
render(f'03_Ground_{mode}_LowSide_Clay.png',cam)
cam.location=(2,-26,.5);cam.rotation_euler=(Vector((0,-1,-2.8))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=34
fill_data=bpy.data.lights.new('V3 Temporary neutral cliff inspection fill','AREA');fill_data.energy=1700;fill_data.shape='DISK';fill_data.size=12
fill=bpy.data.objects.new('V3 Temporary neutral cliff inspection fill',fill_data);rig.objects.link(fill)
fill.location=(-8,-17,1);fill.rotation_euler=(Vector((0,-3,-2.5))-fill.location).to_track_quat('-Z','Y').to_euler()
render(f'05_Ground_{mode}_CliffVolume_Clay.png',cam)
fill.hide_render=True
cam.location=(-2,-15,9);cam.rotation_euler=(Vector((-2.4,-4,.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=60
render(f'04_Ground_{mode}_Junction_Clay.png',cam)
print(json.dumps(report,indent=2))
