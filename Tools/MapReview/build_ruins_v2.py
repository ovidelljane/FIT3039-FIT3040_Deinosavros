"""Original, editable ruins geometry. Source files are never overwritten.

Run inside Blender: --python build_ruins_v2.py -- OUTPUT_DIRECTORY [sample|full]
The full pass is deliberately gated by a reviewed sample evidence file.
"""
import bpy
import bmesh
import json
import math
import random
import sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.geometry import delaunay_2d_cdt
from mathutils.bvhtree import BVHTree

TAU = math.tau
args = sys.argv[sys.argv.index('--') + 1:]
out = Path(args[0]).resolve()
out.mkdir(parents=True, exist_ok=True)
mode = args[1] if len(args) > 1 else 'sample'
source = bpy.data.scenes.get('Map_Rebuild_20260924') or bpy.context.scene
bpy.context.window.scene=source
bpy.context.view_layer.update()
source_path = bpy.data.filepath
anchor_snapshot = {o.name: [list(row) for row in o.matrix_world] for o in source.objects if o.name.startswith('ANCHOR_')}
portal_snapshot = [list(row) for row in source.objects['Portal - carved solid ring'].matrix_world]
terraces = [source.objects[f'Node terrace {i:02d}'].location.copy() for i in range(13)]
routes = [(0,1),(0,2),(0,3),(1,4),(2,5),(3,6),(2,4),(3,5),(4,7),(5,8),(6,9),(4,8),(5,9),(7,10),(8,11),(9,12),(10,13),(11,13),(12,13)]
centers = terraces + [Vector((14,0,1.25))]


def material(name, color, roughness=.82, variation=.12):
    existing=bpy.data.materials.get('V2 '+name)
    if existing is not None:return existing
    mat = bpy.data.materials.new('V2 ' + name)
    mat.diffuse_color = (*color, 1)
    mat.roughness = roughness
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = 0
    bsdf.inputs['Specular IOR Level'].default_value = .22
    bsdf.inputs['Sheen Weight'].default_value = 0
    geom = nodes.new('ShaderNodeTexCoord')
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = .72
    noise.inputs['Detail'].default_value = 1.0
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = .2
    ramp.color_ramp.elements[1].position = .8
    ramp.color_ramp.elements[0].color = (*(c*(1-variation) for c in color),1)
    ramp.color_ramp.elements[1].color = (*(c*(1+variation) for c in color),1)
    links.new(geom.outputs['Object'], noise.inputs['Vector'])
    links.new(noise.outputs['Fac'], ramp.inputs[0])
    links.new(ramp.outputs['Color'], bsdf.inputs['Base Color'])
    return mat


stone = material('Limestone broad planes', (.31,.30,.25), .82, .17)
fracture = material('Exposed fracture', (.385,.365,.29), .87, .11)
rock = material('Cliff grey umber', (.21,.235,.215), .90, .14)
soil = material('Warm silt', (.155,.123,.079), .90, .15)
moss = material('Rooted moss', (.075,.115,.035), .91, .18)
leaf = material('Mature olive leaf', (.055,.125,.022), .78, .17)
new_leaf = material('New leaf tips', (.16,.255,.050), .79, .15)
old_leaf = material('Old fronds', (.105,.12,.028), .87, .13)
bark = material('Fibrous palm trunk', (.245,.17,.095), .9)
clay = material('Neutral clay', (.44,.44,.44), .86, 0)


class Mesh:
    def __init__(self):
        self.v, self.f, self.mi = [], [], []

    def face(self, indices, mat=0):
        self.f.append(tuple(indices))
        self.mi.append(mat)

    def ring_surface(self, rings, mat=0, caps=True, cap_mat=None):
        start, n = len(self.v), len(rings[0])
        for ring in rings:
            self.v.extend(tuple(p) for p in ring)
        for j in range(len(rings)-1):
            for i in range(n):
                k = (i+1) % n
                self.face((start+j*n+i,start+j*n+k,start+(j+1)*n+k,start+(j+1)*n+i), mat)
        if caps:
            self.face(tuple(start+i for i in reversed(range(n))), mat)
            self.face(tuple(start+(len(rings)-1)*n+i for i in range(n)), cap_mat if cap_mat is not None else mat)

    def object(self, name, collection, materials, bevel=0, smooth=False):
        mesh = bpy.data.meshes.new(name + ' geometry')
        mesh.from_pydata(self.v, [], self.f)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        for mat in materials:
            mesh.materials.append(mat)
        for p, index in zip(mesh.polygons, self.mi):
            p.material_index = min(index, len(materials)-1)
            p.use_smooth = smooth
        if bevel:
            mod = obj.modifiers.new('Worn structural edges', 'BEVEL')
            mod.width, mod.segments = bevel, 2
            mod.limit_method = 'ANGLE'
        obj['original_geometry'] = True
        return obj


def collection(name, scene):
    col = bpy.data.collections.new(name)
    scene.collection.children.link(col)
    return col


def prism(mesh, outline, bottom, top, mat=0):
    mesh.ring_surface([[(x,y,bottom) for x,y in outline],
                       [(x,y,top if isinstance(top,(int,float)) else top[i]) for i,(x,y) in enumerate(outline)]],mat)


def tube(mesh, points, radii, mat=0, sides=8):
    rings = []
    points = [Vector(p) for p in points]
    for i,p in enumerate(points):
        tangent = (points[min(i+1,len(points)-1)] - points[max(0,i-1)]).normalized()
        right = tangent.cross(Vector((0,0,1)))
        if right.length < .05:
            right = tangent.cross(Vector((0,1,0)))
        right.normalize()
        up = tangent.cross(right).normalized()
        rings.append([p + radii[i]*(right*math.cos(a*TAU/sides)+up*math.sin(a*TAU/sides)) for a in range(sides)])
    mesh.ring_surface(rings, mat)


def planar_chip(obj,point,normal,material_index=1):
    bm=bmesh.new();bm.from_mesh(obj.data)
    result=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
        dist=1e-6,plane_co=point,plane_no=normal,clear_outer=True,clear_inner=False)
    boundary=[edge for edge in result['geom_cut'] if isinstance(edge,bmesh.types.BMEdge) and edge.is_boundary]
    if boundary:
        fill=bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
        for face in fill['faces']:face.material_index=material_index
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(obj.data);bm.free();obj.data.update()


def column(col, name, pos, height=5.4, broken=False, seed=1):
    mesh = Mesh()
    # Broad fracture planes replace waves around a clipped cylinder.
    n=128
    shaft_radius=lambda z:.635-.105*max(0,min(1,(z-.64)/4.1))
    def cut_height(x,y):
        return height+.52*x-.28*y
    def ring(z,r,flute=0,square=False,cut=False):
        result=[]
        for i in range(n):
            a=TAU*i/n
            radius=r-flute*(.5+.5*math.cos(a*16))
            if square:radius=r/max(abs(math.cos(a)),abs(math.sin(a)))
            x,y=radius*math.cos(a),radius*math.sin(a)
            # Worn corners are clipped planes, not repeated chips around every edge.
            if square and x+y>r*1.57:
                excess=(x+y-r*1.57)*.5;x-=excess;y-=excess
            if square and -x+y>r*1.78:
                excess=(-x+y-r*1.78)*.5;x+=excess;y-=excess
            zz=cut_height(x,y) if cut else z
            result.append((x,y,zz))
        return result
    base=[ring(0,.90,square=True),ring(.14,.91,square=True),ring(.23,.86,square=True),
          ring(.28,.76),ring(.37,.71),ring(.46,.67),ring(.62,.635,.04)]
    if broken:
        levels=sorted(set(z for z in [.82,1.25,height-.58] if .62<z<height-.34))
        shaft=[ring(z,shaft_radius(z),.048) for z in levels]
        shaft.append(ring(height,shaft_radius(height),.048,cut=True))
        profiles=base+shaft
    else:
        profiles=base+[ring(z,shaft_radius(z),.048) for z in [.82,1.8,3.2,4.5,4.74]]
        profiles += [ring(4.81,.54),ring(4.86,.58),ring(5.02,.72),ring(5.13,.76),
                     ring(5.19,.82,square=True),ring(5.39,.82,square=True),ring(5.43,.78,square=True)]
    mesh.ring_surface(profiles,0,True,1 if broken else 0)
    obj=mesh.object(name,col,[stone,fracture],.018)
    obj.location=pos
    obj['structural_role']='standing column; fracture shared with fallen segment'
    if broken:
        # One post-collapse edge spall, cut as a true plane with a closed planar cap.
        planar_chip(obj,(.49,-.02,height+.18),(.72,-.18,1.0))
        fallen=Mesh()
        remaining=[ring(height,shaft_radius(height),.048,cut=True)]
        remaining += [ring(z,shaft_radius(z),.048) for z in sorted(set([height+.65,4.5,4.74])) if height+.34<z<=4.74]
        remaining += [ring(4.81,.54),ring(4.86,.58),ring(5.02,.72),ring(5.13,.76),
                      ring(5.19,.82,square=True),ring(5.39,.82,square=True),ring(5.43,.78,square=True)]
        remaining=[[(x,y,z-height) for x,y,z in r] for r in remaining]
        fallen.ring_surface(remaining,0,True)
        fallen.mi[-2]=1
        fall=fallen.object(name+' matching fallen shaft',col,[stone,fracture],.018)
        fall.rotation_euler=(math.radians(84),0,math.radians(55))
        fall.location=(pos[0]+.12,pos[1]+.78,.7)
        fall['source_break']=name
        fall['matching_fracture_profile']=True
    return obj


def platform(col,name,pos,seed=1):
    mesh=Mesh()
    # A paved court is an architectural floor, not a circular object placed on dirt.
    panels=[([(-1.65,-1.05),(-1.30,-1.51),(-.18,-1.48),(-.10,-.14),(-1.62,-.16)],.13),
            ([( -.13,-1.48),(1.17,-1.46),(1.66,-1.09),(1.62,-.24),(-.05,-.15)],.12),
            ([(-1.60,-.10),(-.08,-.09),(.14,1.45),(-1.18,1.54),(-1.65,1.05)],.105),
            ([(-.02,-.10),(1.61,-.19),(1.64,.94),(1.30,1.42),(.20,1.45)],.11)]
    if seed%3==1:
        panels=[([(-1.65,-1.05),(-1.3,-1.5),(.45,-1.5),(.43,-.10),(-1.62,-.17)],.13),
                ([(.49,-1.48),(1.18,-1.45),(1.66,-1.07),(1.65,.55),(.49,.48)],.12),
                ([(-1.62,-.11),(.44,-.04),(.43,1.44),(-1.18,1.53),(-1.66,1.02)],.11),
                ([(.50,.54),(1.66,.61),(1.61,1.05),(1.27,1.43),(.48,1.44)],.105)]
    elif seed%3==2:
        panels=[([(-1.65,-1.02),(-1.30,-1.49),(1.18,-1.45),(1.65,-1.05),(1.63,-.37),(-1.65,-.41)],.125),
                ([(-1.65,-.35),(-.56,-.34),(-.31,1.48),(-1.18,1.53),(-1.66,1.03)],.11),
                ([(-.50,-.33),(1.63,-.30),(1.66,.95),(1.27,1.43),(-.25,1.49)],.12)]
    angle=math.radians((seed%5-2)*2.5)
    for outline,z in panels:
        outline=[(x*math.cos(angle)-y*math.sin(angle),x*math.sin(angle)+y*math.cos(angle)) for x,y in outline]
        prism(mesh,outline,-.12,z)
    obj=mesh.object(name,col,[stone],.022)
    obj.location=pos
    obj['node_center']=list(pos)
    return obj


def path(col,name,a,b,seed=0):
    a,b=Vector(a),Vector(b)
    delta=b-a;delta.z=0
    direction=delta.normalized();side=Vector((-direction.y,direction.x,0))
    start=a+direction*1.5; length=max(.8,delta.length-3)
    mesh=Mesh();rng=random.Random(seed)
    count=max(2,round(length/1.1))
    for i in range(count):
        for row in range(2):
            u0=i/count*length+.018;u1=(i+1)/count*length-.018
            v0=-.64+row*.64+.018;v1=v0+.59
            corners=[(u0+.035,v0),(u1-.06,v0-.025),(u1,v0+.09),(u1-.025,v1),(u0+.03,v1+.015),(u0,v1-.1)]
            outline=[]
            for u,v in corners:
                p=start+direction*u+side*v
                outline.append((p.x,p.y))
            height=.10+rng.uniform(-.016,.015)
            prism(mesh,outline,-.12,height)
    return mesh.object(name,col,[stone],.035)


def palm(col,name,pos,variant=0,seed=1,height=4.8):
    rng=random.Random(seed)
    origin=Vector(pos)
    lean=Vector((.48+.18*variant,.18*(variant-1),0))
    trunk=Mesh()
    points=[origin+lean*(t*t)+Vector((0,0,height*t)) for t in [i/18 for i in range(19)]]
    radii=[.18*(1-.32*i/18)*(1+.023*math.sin(i*2.7)) for i in range(19)]
    tube(trunk,points,radii,0,12)
    trunk_obj=trunk.object(name+' continuous trunk',col,[bark],smooth=True)
    crown=points[-1]
    stems=Mesh();leaves=Mesh()
    # Distinct age layers and uneven phyllotaxis. Every leaflet starts on its rachis.
    layouts=[[(3,1.55,1.75),(4,3.0,.50),(2,2.45,-1.6)],
             [(2,1.55,1.55),(5,3.4,.25),(2,2.75,-1.6)],
             [(2,1.5,1.15),(4,2.65,.35),(4,2.95,-1.9)]]
    index=0
    for age,(count,length,rise) in enumerate(layouts[variant%3]):
        for j in range(count):
            angle=index*2.399963+rng.uniform(-.19,.19)+variant*.6
            index+=1
            d=Vector((math.cos(angle),math.sin(angle),0));s=Vector((-d.y,d.x,0))
            frond_length=length*rng.uniform(.90,1.09)
            def axis(t):
                return crown + d*(frond_length*t) + s*(.20*math.sin(t*math.pi)*(-1 if j%2 else 1)) + Vector((0,0,rise*t+.64*math.sin(math.pi*t)-.58*t*t))
            rachis=[axis(t/18) for t in range(19)]
            tube(stems,rachis,[.034*(1-t/20) for t in range(19)],0,7)
            pairs=10 if age==0 else 14
            for k in range(pairs):
                t=.17+.78*k/pairs+rng.uniform(-.007,.007)
                width=.81*math.sin(math.pi*t)**.72*(frond_length/3.2)
                for sign in [-1,1]:
                    tt=t+(.014 if sign>0 else 0)
                    root=axis(tt)
                    end=root+s*(sign*width)+d*(width*.51)+Vector((0,0,-width*(.35+age*.18)))
                    leaf_dir=end-root
                    normal=leaf_dir.cross(Vector((0,0,1))).normalized()
                    half=.058*(.6+math.sin(math.pi*t))*(frond_length/3.2)
                    start=len(leaves.v)
                    for q in range(9):
                        u=q/8
                        mid=root+leaf_dir*u+Vector((0,0,.16*math.sin(u*math.pi)*width))
                        # Parallel-sided young blade tapers to a narrow drooping tip.
                        shape=min(1,u/.16)*(1-u)**.65
                        span=half*shape if q not in [0,8] else .006 if q==0 else .004
                        ridge=.002+.013*math.sin(u*math.pi)
                        leaves.v.extend([tuple(mid-normal*span),tuple(mid+Vector((0,0,ridge))),tuple(mid+normal*span)])
                    for q in range(8):
                        for l in range(2):
                            v=start+q*3+l
                            leaves.face((v,v+1,v+4,v+3),0 if age==1 else 1 if age==0 else 2)
    stems.object(name+' connected petioles and rachises',col,[bark],smooth=True)
    obj=leaves.object(name+' attached broad pinnae',col,[leaf,new_leaf,old_leaf])
    sol=obj.modifiers.new('Leaf lamina', 'SOLIDIFY');sol.thickness=.006
    obj['growth_variant']=variant
    obj['leaflet_attachment']='All roots sampled on the matching rachis centerline'
    return trunk_obj


def fern(col,name,pos,seed=1,scale=1):
    rng=random.Random(seed);stems=Mesh();leaves=Mesh();origin=Vector(pos)
    for j in range(7):
        a=j*2.4+rng.uniform(-.3,.3);d=Vector((math.cos(a),math.sin(a),0));s=Vector((-d.y,d.x,0))
        length=rng.uniform(.65,1.25)*scale
        def axis(t):return origin+d*(length*t)+Vector((0,0,length*(.6*math.sin(t*math.pi*.9)+.12*t)))
        tube(stems,[axis(i/12) for i in range(13)],[.016*scale*(1-i/14) for i in range(13)],0,6)
        for k in range(10):
            t=.16+.075*k
            for sign in [-1,1]:
                r=axis(t);w=.26*scale*math.sin(math.pi*t)
                tip=r+s*(sign*w)+d*w*.5+Vector((0,0,.02))
                m=(r+tip)*.5
                start=len(leaves.v)
                leaves.v.extend(tuple(p) for p in [r,m+d*.06*scale,tip,m-d*.075*scale,m+Vector((0,0,.027*scale))])
                for q in range(4):leaves.face((start+q,start+(q+1)%4,start+4),j%2)
    stems.object(name+' rooted frond axes',col,[bark],smooth=True)
    obj=leaves.object(name+' pinnae',col,[leaf,new_leaf])
    sol=obj.modifiers.new('Thin leaves','SOLIDIFY');sol.thickness=.005


def broadleaf_clump(col,name,pos,seed=1,scale=1):
    rng=random.Random(seed);origin=Vector(pos);stems=Mesh();leaves=Mesh()
    for j in range(6):
        a=j*2.399+rng.uniform(-.2,.2);direction=Vector((math.cos(a),math.sin(a),0))
        sideways=Vector((-direction.y,direction.x,0))
        length=rng.uniform(.67,1.12)*scale
        base=origin+direction*.12*scale
        petiole_end=base+direction*length*.28+Vector((0,0,length*.5))
        tube(stems,[base,(base+petiole_end)*.5,petiole_end],[.022*scale,.018*scale,.010*scale],0,6)
        start=len(leaves.v)
        for k in range(9):
            t=k/8
            middle=petiole_end+direction*length*t+Vector((0,0,length*(.23*math.sin(math.pi*t)-.28*t)))
            width=.29*length*math.sin(math.pi*t)**.76 if k not in [0,8] else .008
            ridge=.003+.035*math.sin(math.pi*t)
            leaves.v.extend(tuple(v) for v in [middle-sideways*width,middle+Vector((0,0,ridge)),middle+sideways*width])
        for k in range(8):
            for side in range(2):
                p=start+k*3+side;leaves.face((p,p+1,p+4,p+3),j%2)
    stems.object(name+' connected petioles',col,[bark],smooth=True)
    obj=leaves.object(name+' folded blades',col,[leaf,new_leaf])
    solid=obj.modifiers.new('Leaf lamina','SOLIDIFY');solid.thickness=.008


def rock_patch(col,name):
    return terrain(col,name,5.25,3.5,[(0,-.4)],False)


def terrain(col,name,rx,ry,flat_centers,full=True):
    mesh=Mesh();n=44;rings=[]
    def outline(a):
        base=1+.074*math.sin(3*a+.8)+.037*math.sin(7*a-1)+.021*math.cos(11*a)
        return base+(.075*math.sin(2*a)**2 if full else 0)
    def ground(x,y):
        base=-.075+.20*math.sin(x*.55+1)*math.sin(y*.69)+.13*math.sin(x*.89+y*.44)
        base+=.43*math.exp(-((x-rx*.46)**2+(y-ry*.64)**2)/2.8)
        base+=.35*math.exp(-((x+rx*.35)**2+(y+ry*.66)**2)/2.5)
        if full:
            fringe=max(0,min(1,((x/rx)**2+(y/ry)**2-.58)/.30))
            base+=fringe*(.42*math.sin(x*.59+y*.22)+.21*math.sin(y*.85-x*.31))
            for px,py in [(-7.75,3.0),(-7.75,-3.0),(-1.75,2.75),(-2,-3),(3.25,3),(3.25,-2.75),(6.5,3),(6.5,-2.75)]:
                base+=.85*math.exp(-((x-px)**2+(y-py)**2)/1.45)
        for cx,cy in flat_centers:
            r=math.hypot(x-cx,y-cy)
            if r<2.4:
                blend=max(0,min(1,(r-1.4)/1.0))
                burial=0
                if full:
                    side=max(0,math.cos(math.atan2(y-cy,x-cx)-.42*cx))
                    burial=.20*max(0,min(1,(r-1.1)/.60))*side
                base=base*blend+(-.07+burial)*(1-blend)
        if full:
            for ia,ib in routes:
                a,b=centers[ia],centers[ib]
                delta=Vector((b.x-a.x,b.y-a.y));point=Vector((x-a.x,y-a.y))
                t=max(0,min(1,point.dot(delta)/max(1e-8,delta.length_squared)))
                distance=(point-delta*t).length
                if distance<1.35:
                    blend=max(0,min(1,(distance-.69)/.66));base=base*blend-.07*(1-blend)
        if not full and -4.6<x<-.3 and abs(y+.4)<.75:
            base=-.055
        if not full:
            # Foundation seats are level earth; nearby undulations remain outside the plinth.
            for cx,cy in [(-2.6,1.3),(1.05,1.5)]:
                r=max(abs(x-cx),abs(y-cy))
                if r<1.38:
                    blend=max(0,min(1,(r-.98)/.40));base=base*blend-.07*(1-blend)
        return base
    # A sparse set of non-horizontal major planes replaces stacked contour bands.
    for j in range(5 if not full else 0):
        t=j/4
        ring=[]
        for i in range(n):
            a=TAU*i/n
            aa=a+.095*math.sin(a*3+t*3)*(1-t)
            shape=outline(a)
            taper=.45+.55*t+.13*math.sin(t*math.pi)
            bulge=.105*math.sin(aa*7-t*2.2)+.06*math.sin(aa*11+t*3)
            x=math.cos(aa)*rx*(shape*taper+bulge*(1-t))
            y=math.sin(aa)*ry*(shape*taper+bulge*(1-t))
            top=ground(math.cos(a)*rx*shape,math.sin(a)*ry*shape)
            z=top-(1-t)*(6.8 if full else 4.2)+math.sin(a*5+t*2)*.75*math.sin(t*math.pi)
            ring.append((x,y,z))
        rings.append(ring)
    if full:
        rim=list(reversed([(-18,-3),(-18,2),(-17,5.5),(-13,8.3),(-10.5,10),(-6.2,9.6),
            (-2,10.5),(2.5,9.4),(6,10),(10.8,9.3),(13,7.5),(16.1,6.1),(17.6,3.9),
            (18.6,.8),(18,-3.3),(16.2,-6),(12.4,-7.5),(8.7,-8.9),(4.5,-10.2),
            (0,-9.8),(-4.5,-9.5),(-8.4,-8.3),(-11.6,-8.9),(-14.8,-6.6)]))
        n=len(rim);bottom=[];middle=[];top=[]
        for i,(x,y) in enumerate(rim):
            scale=.66+.10*math.sin(i*1.7)
            bottom.append((x*scale+.65*math.sin(i*.8),y*scale+.45*math.cos(i*1.3),-6.8+.95*math.sin(i*1.21)))
            bulge=1.055 if i%5 in [1,2] else .92
            middle.append((x*bulge+.46*math.sin(i*1.8),y*bulge+.30*math.cos(i*1.4),
                           -2.65+1.50*math.sin(i*1.63)+.37*math.cos(i*2.8)))
            top.append((x,y,ground(x,y)))
        rings=[bottom,middle,top]
    mesh.ring_surface(rings,0,False)
    # A constrained, irregular top triangulation joins the existing cliff boundary.
    # Dense enough for slopes, but not a radial fan or a stack of paper planes.
    boundary=[Vector((p[0],p[1])) for p in rings[-1]]
    points=boundary[:]
    rng=random.Random(224)
    for ix in range(-16,17):
        for iy in range(-12,13):
            x=rx*(ix+rng.uniform(-.34,.34))/17;y=ry*(iy+rng.uniform(-.34,.34))/13
            if (x/rx)**2+(y/ry)**2<.82:points.append(Vector((x,y)))
    verts,_,faces,_,_,_=delaunay_2d_cdt(points,[(i,(i+1)%n) for i in range(n)],[list(range(n))],1,1e-6)
    top_indices=[]
    for p in verts:
        hit=next((i for i,b in enumerate(boundary) if (p-b).length<1e-5),None)
        if hit is not None:top_indices.append((len(rings)-1)*n+hit)
        else:
            top_indices.append(len(mesh.v));mesh.v.append((p.x,p.y,ground(p.x,p.y)))
    for face in faces:
        ids=[top_indices[i] for i in face]
        x=sum(mesh.v[i][0] for i in ids)/len(ids);y=sum(mesh.v[i][1] for i in ids)/len(ids)
        damp=math.exp(-((x-rx*.56)**2+(y-ry*.38)**2)/1.7)
        damp+=math.exp(-((x+rx*.45)**2+(y-ry*.25)**2)/1.2)
        mesh.face(ids,2 if damp>.35 else 1)
    mesh.face(tuple(reversed(range(n))),0)
    obj=mesh.object(name,col,[rock,soil,moss],0)
    for p in obj.data.polygons:
        p.use_smooth=p.material_index>0
    return obj


def seat_on_ground(obj,ground,embed=.045):
    bpy.context.view_layer.update()
    tree=BVHTree.FromObject(ground,bpy.context.evaluated_depsgraph_get())
    inv=ground.matrix_world.inverted()
    contacts=[]
    for vertex in obj.data.vertices:
        p=obj.matrix_world@vertex.co
        hit,_,_,_=tree.ray_cast(inv@Vector((p.x,p.y,20)),Vector((0,0,-1)),40)
        if hit is not None:contacts.append(p.z-(ground.matrix_world@hit).z)
    if not contacts:raise RuntimeError('No ground under '+obj.name)
    obj.location.z-=min(contacts)+embed
    obj['ground_contact']='Lowest visible surface seated against the actual terrain mesh'


def rooted_fern(col,name,pos,ground,seed,scale):
    bpy.context.view_layer.update()
    tree=BVHTree.FromObject(ground,bpy.context.evaluated_depsgraph_get())
    hit,_,_,_=tree.ray_cast(Vector((pos[0],pos[1],20)),Vector((0,0,-1)),40)
    if hit is None:raise RuntimeError('No terrain under rooted fern '+name)
    fern(col,name,(pos[0],pos[1],hit.z+.005),seed,scale)


def portal_frame(col,name,transform=None):
    mesh=Mesh();n=192;rings=[]
    # The original 2.09 aperture stays open. Haunches thicken into grounded feet.
    profiles=[(2.09,-.29),(2.14,-.44),(2.28,-.44),(2.31,-.39),(2.43,-.39),
              (2.46,-.44),(2.91,-.35),(2.99,0),(2.91,.47),(2.09,.47)]
    for pi,(radius,depth) in enumerate(profiles):
        ring=[]
        for i in range(n):
            a=TAU*i/n
            damage=.25*max(0,1-abs(a-.78)/.14)+.14*max(0,1-abs(a-3.35)/.11)
            rr=radius-(damage if pi in [5,6,7,8] else 0)
            # A recessed Greek key band; no floating ornament meshes.
            phase=(i%16)/16
            groove= .045 if pi in [3,4] and .15<phase<.8 else 0
            x,y=rr*math.cos(a),rr*math.sin(a)
            if 5<pi<9:
                # An integral flat lower chord creates a broad masonry bearing surface.
                y=max(-2.62,y)
            ring.append((x,y,-depth-groove))
        rings.append(ring)
    mesh.ring_surface(rings,0,False)
    base=(len(rings)-1)*n
    for i in range(n):mesh.face((base+i,base+(i+1)%n,(i+1)%n,i))
    obj=mesh.object(name,col,[stone],.014)
    if transform is not None:obj.matrix_world=transform
    obj['aperture_radius']=2.09
    return obj


def camera(scene,col,name,location,target,lens=45):
    data=bpy.data.cameras.new(name);data.lens=lens
    obj=bpy.data.objects.new(name,data);col.objects.link(obj)
    obj.location=location;obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj


def setup_light(scene,col):
    world=bpy.data.worlds.new('V2 neutral overcast');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.28,.32,.37,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.45
    scene.world=world
    for name,loc,power,size,color in [('Key',(-8,-10,14),1900,8,(1,.94,.83)),('Fill',(5,-2,9),1000,9,(.78,.86,1)),('Rim',(0,9,12),1800,7,(.85,.92,1))]:
        data=bpy.data.lights.new('V2 '+name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
        obj=bpy.data.objects.new('V2 '+name,data);col.objects.link(obj);obj.location=loc
        obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
    scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    scene.render.image_settings.file_format='PNG'


def render(scene,cam,path,override=None):
    scene.camera=cam;scene.view_layers[0].material_override=override
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True,scene=scene.name)
    scene.view_layers[0].material_override=None


def copy_source_object(name,col):
    original=source.objects[name]
    obj=original.copy();obj.name='V2 '+name
    obj.parent=None;obj.matrix_world=original.matrix_world.copy()
    col.objects.link(obj)
    obj['canonical_name']=name
    return obj


def wall_mass(col,name,outline,heights,bottom=-.24):
    mesh=Mesh();prism(mesh,outline,bottom,heights)
    obj=mesh.object(name,col,[stone,fracture],.035)
    obj['structural_role']='Continuous load-bearing masonry remnant'
    return obj


def beam(col,name,x0,x1,y,z,broken_end=False):
    # A profiled entablature is one mass, not a pile of individual decorative blocks.
    mesh=Mesh()
    outline=[(x0,y-.45),(x1,y-.45),(x1-.18 if broken_end else x1,y+.43),(x0,y+.43)]
    rings=[]
    for depth,height in [(0,0),(.035,.12),(.0,.20),(.08,.50),(.09,.63)]:
        rings.append([(x+(-depth if i in [0,3] else depth),yy+(-depth if i<2 else depth),z+height)
                      for i,(x,yy) in enumerate(outline)])
    mesh.ring_surface(rings)
    obj=mesh.object(name,col,[stone],.023)
    return obj


def broken_entrance(col):
    mesh=Mesh();outer=2.8;inner=1.84;spring=4.0;end=.61
    outline=[(-outer,0),(-outer,spring)]
    outline += [(outer*math.cos(math.pi+(end-math.pi)*i/32),spring+outer*math.sin(math.pi+(end-math.pi)*i/32)) for i in range(1,33)]
    outline += [(inner*math.cos(end+(math.pi-end)*i/32),spring+inner*math.sin(end+(math.pi-end)*i/32)) for i in range(33)]
    outline.append((-inner,0))
    mesh.ring_surface([[(x,-.43,z) for x,z in outline],[(x,.43,z) for x,z in outline]])
    obj=mesh.object('V2 Entrance fractured arch and jamb',col,[stone,fracture],.026)
    obj.matrix_world=source.objects['Western ruin - continuous arch'].matrix_world.copy()
    right=Mesh()
    right.ring_surface([[(1.84,-.43,0),(2.8,-.43,0),(2.8,.43,0),(1.84,.43,0)],
                        [(1.84,-.43,3.52),(2.8,-.43,3.13),(2.8,.43,3.19),(1.84,.43,3.58)]],0,True,1)
    jamb=right.object('V2 Entrance broken opposite jamb',col,[stone,fracture],.022)
    jamb.matrix_world=obj.matrix_world.copy()
    fallen=Mesh()
    arc=[(outer*math.cos(a),spring+outer*math.sin(a)) for a in [end*i/18 for i in range(19)]]
    arc += [(inner*math.cos(a),spring+inner*math.sin(a)) for a in [end*(1-i/18) for i in range(19)]]
    center=Vector((sum(p[0] for p in arc)/len(arc),sum(p[1] for p in arc)/len(arc)))
    fallen.ring_surface([[(x-center.x,-.43,z-center.y) for x,z in arc],[(x-center.x,.43,z-center.y) for x,z in arc]])
    fall=fallen.object('V2 Entrance corresponding fallen arch section',col,[stone,fracture],.022)
    fall.location=(-16.1,6.8,.5);fall.rotation_euler=(math.radians(82),.12,math.radians(46))
    fall['source_break']=obj.name
    return fall


def unique_architecture_uvs(scene,col):
    for obj in col.objects:
        if obj.type!='MESH':continue
        if obj.data.users>1:obj.data=obj.data.copy()
        for other in scene.objects:other.select_set(False)
        obj.select_set(True);bpy.context.view_layer.objects.active=obj
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=.66,island_margin=.018,area_weight=.3,correct_aspect=True,scale_to_bounds=True)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj['uv_workflow']='Unique non-overlapping structural projection; reserved for original carving and wear bakes'


def build_full_environment():
    gate_path=out/'sample-gate.json'
    if not gate_path.exists():raise RuntimeError('Sample gate has not been recorded.')
    gate=json.loads(gate_path.read_text(encoding='utf-8'))
    if not gate.get('allow_full_scene') or not Path(gate.get('real_ui_capture','')).is_file():
        raise RuntimeError('Full scene requires recorded sample and real UI inspection.')
    previous=bpy.data.scenes.get('V2 Environment Review')
    if previous:
        if mode!='full-revise':raise RuntimeError('A V2 full scene already exists. Use the explicit review revision mode.')
        revision_copy=out/'geometry-pass01.blend'
        if not revision_copy.exists():bpy.ops.wm.save_as_mainfile(filepath=str(revision_copy),copy=True)
        for obj in list(previous.objects):
            if len(obj.users_scene)!=1:raise RuntimeError('Shared object prevents isolated review rebuild: '+obj.name)
        for obj in list(previous.objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.scenes.remove(previous)
    scene=bpy.data.scenes.new('V2 Environment Review');bpy.context.window.scene=scene
    land=collection('V2 Environment terrain',scene)
    road=collection('V2 Environment route courts',scene)
    architecture=collection('V2 Environment architecture',scene)
    plants=collection('V2 Environment vegetation',scene)
    anchors=collection('V2 Gameplay anchors',scene)
    retained=collection('V2 Preserved sanctuary structure',scene)
    rig=collection('V2 Environment review rig',scene)
    background=collection('Review Background',scene)
    context_root=bpy.data.objects.new('V2 Review Background',None);background.objects.link(context_root)
    context_root['canonical_name']='Review Background'
    context_root['review_background']=True
    ground=terrain(land,'V2 Island continuous fractured bedrock',18.9,10.5,[(p.x,p.y) for p in terraces],True)
    for i,pos in enumerate(terraces):
        obj=platform(road,f'V2 Node terrace {i:02d}',pos,100+i)
        obj['canonical_name']=f'Node terrace {i:02d}'
    for i,(ia,ib) in enumerate(routes):
        obj=path(road,f'V2 Paved route {i:02d}',centers[ia],centers[ib],220+i)
        obj['canonical_name']=f'Paved route {i:02d}'
    for name in anchor_snapshot:copy_source_object(name,anchors)
    reference_height=source.objects['ANCHOR_Node_L03_02'].matrix_world.translation.z
    for number,index in [('L02_03',2),('L04_03',8),('L05_03',11)]:
        obj=bpy.data.objects.new('V2 ANCHOR_Node_'+number,None);anchors.objects.link(obj)
        obj.location=(terraces[index].x,terraces[index].y,reference_height)
        obj['canonical_name']='ANCHOR_Node_'+number
        obj['derived_from_platform']=f'Node terrace {index:02d}'
    for name in ['Sanctuary carved approach stair','Portal - recessed luminous inner rim',
                 'Portal - energy veil','Offering brazier 00','Offering brazier 01','Brazier flame 00','Brazier flame 01']:
        copy_source_object(name,retained)
    foundation=wall_mass(architecture,'V2 Sanctuary foundation',
        [(-3.25,-2.22),(-2.7,-3.20),(1.65,-3.13),(3.24,-2.24),(3.12,2.25),(1.55,3.15),
         (-2.8,3.18),(-3.3,2.25),(-3.3,1.5),(-1.9,1.5),(-1.9,-1.5),(-3.25,-1.5)],
        [1.25]*12,-.22)
    foundation.matrix_world=source.objects['Sanctuary foundation'].matrix_world.copy()
    foundation['canonical_name']='Sanctuary foundation'
    upper_steps=Mesh()
    prism(upper_steps,[(11.55,-1.45),(11.87,-1.45),(11.87,1.45),(11.55,1.45)],-.12,1.0775)
    prism(upper_steps,[(11.87,-1.45),(12.20,-1.45),(12.20,1.45),(11.87,1.45)],-.12,1.25)
    upper_steps.object('V2 Sanctuary upper approach landing',architecture,[stone],.018)
    portal=portal_frame(architecture,'V2 Portal - carved solid ring',Matrix(portal_snapshot))
    original_portal=source.objects['Portal - carved solid ring']
    portal.rotation_mode=original_portal.rotation_mode
    portal.location=original_portal.location.copy()
    portal.rotation_euler=original_portal.rotation_euler.copy()
    portal.scale=original_portal.scale.copy()
    portal['canonical_name']='Portal - carved solid ring'
    # Rear architectural order has a common footing, two standing bays and one fallen bay.
    wall_mass(architecture,'V2 Rear continuous colonnade footing',
              [(-11.7,7.55),(11.3,7.55),(11.45,8.95),(-11.8,9.05)],[.12,.12,.22,.19])
    rear=[(-10.3,False,5.4),(-5.25,False,5.4),(-.25,True,2.7),(9.55,True,3.55)]
    for i,(x,broken,height) in enumerate(rear):
        obj=column(architecture,f'V2 Rear colonnade order {i:02d}',(x,8.10,.08),height,broken,300+i)
        if broken:
            fallen=architecture.objects[obj.name+' matching fallen shaft']
            fallen.rotation_euler=(math.radians(84),0,math.radians(110 if i==2 else 245))
            fallen.location=(x+.4,8.35,.7);seat_on_ground(fallen,ground,.11)
    wall_mass(architecture,'V2 Rear vacant bay foundation',[(3.77,7.22),(5.53,7.22),(5.53,8.98),(3.77,8.98)],
              [.26,.24,.25,.28],.08)
    lintel=beam(architecture,'V2 Rear surviving fractured entablature',-11.1,-3.45,8.1,5.51,True)
    planar_chip(lintel,(-3.60,8.10,5.95),(.85,.22,.55),0)
    # The western arch continues into wall roots. No changes to the start node.
    entrance_fall=broken_entrance(architecture)
    seat_on_ground(entrance_fall,ground,.07)
    wall_mass(architecture,'V2 Entrance north wall root',[(-16.40,4.85),(-15.53,4.88),(-15.49,7.22),(-16.38,7.35)],
              [2.2,2.1,.60,.83])
    wall_mass(architecture,'V2 Entrance south wall root',[(-16.38,-2.48),(-15.55,-2.44),(-15.52,.10),(-16.36,.05)],
              [.74,.91,1.65,1.72])
    column(architecture,'V2 Entrance broken order',(-15.1,-4.0,-.12),2.3,True,317)
    fallen=architecture.objects['V2 Entrance broken order matching fallen shaft']
    fallen.location=(-15.6,-4.7,.7);fallen.rotation_euler=(math.radians(84),0,math.radians(22));seat_on_ground(fallen,ground,.11)
    # The sanctuary opening stays unobstructed; side remnants are outside its clear aperture.
    wall_mass(architecture,'V2 Sanctuary north retaining return',[(12.2,3.3),(15.4,3.3),(15.7,3.95),(12.1,4.0)],
              [.7,2.10,2.05,.78])
    wall_mass(architecture,'V2 Sanctuary south retaining return',[(14.1,-3.8),(16.7,-3.3),(16.7,-2.55),(14.2,-3.0)],
              [1.35,.74,.72,1.44])
    column(architecture,'V2 Sanctuary rear order',(16.55,2.70,.78),3.9,True,334)
    fallen=architecture.objects['V2 Sanctuary rear order matching fallen shaft']
    fallen.location=(16.2,3.1,.7);fallen.rotation_euler=(math.radians(84),0,math.radians(215));seat_on_ground(fallen,ground,.10)
    palm_specs=[((-14.0,-6.0,-.1),0,4.75),((-11.8,6.35,-.1),1,5.6),((-5.0,-8.1,-.1),2,4.3),
                ((2.4,-8.65,-.1),0,4.6),((12.3,6.3,-.1),2,5.6),((16.2,-4.6,-.1),1,4.5)]
    for i,(pos,variant,height) in enumerate(palm_specs):
        prefix=f'V2 Palm {i:02d}'
        trunk=palm(plants,prefix,pos,variant,400+i,height)
        seat_on_ground(trunk,ground,.07)
        for obj in plants.objects:
            if obj.name.startswith(prefix) and obj!=trunk:obj.location.z+=trunk.location.z
        for j,(dx,dy,size) in enumerate([(-.45,-.27,.9),(.39,.18,.67),(.12,-.52,.78)]):
            rooted_fern(plants,f'V2 Palm root understory {i:02d} {j}',(pos[0]+dx,pos[1]+dy,0),ground,500+i*4+j,size)
    for i,(x,y,size) in enumerate([(-9.5,7.3,.8),(-5.1,7.3,.75),(-.9,7.8,.75),(5.2,7.5,.85),
                                  (-15.2,5.5,.95),(-15.0,-2.3,.8),(13.3,3.8,.95),(15.8,-3.2,.7)]):
        rooted_fern(plants,f'V2 Masonry root understory {i:02d}',(x,y,-.06),ground,550+i,size)
    # Three irregular root/ruin patches replace one flowerpot-like clump in every route gap.
    for i,(x,y) in enumerate([(-12.0,5.05),(1.3,8.55),(14.85,-5.1)]):
        for j,(dx,dy,scale) in enumerate([(-.6,.22,1.08),(.39,-.18,.92),(-.22,-.43,.80),(.95,.32,.74),(-.77,-.4,.63)]):
            rooted_fern(plants,f'V2 Courtyard soil pocket {i:02d} {j}',(x+dx,y+dy,0),ground,740+i*4+j,scale)
        tree=BVHTree.FromObject(ground,bpy.context.evaluated_depsgraph_get())
        hit,_,_,_=tree.ray_cast(Vector((x+.30,y+.32,20)),Vector((0,0,-1)),40)
        if hit is not None:broadleaf_clump(plants,f'V2 Courtyard broadleaf {i:02d}',(x+.30,y+.32,hit.z),800+i,.8)
    for obj in source.objects:
        if obj.type=='MESH' and any(c.name=='08 Background' for c in obj.users_collection) and 'density container' not in obj.name:
            copied=copy_source_object(obj.name,background)
            copied.name='V2 Background '+obj.name
            copied['canonical_name']='Background '+obj.name
            copied.parent=context_root;copied['review_background']=True
    background_source=out.parent.parent/'MapScene.blend'
    if background_source.is_file():
        with bpy.data.libraries.load(str(background_source),link=False) as (available,loaded):
            loaded.objects=[name for name in ['Chasm floor','Low valley haze'] if name in available.objects]
        for obj in loaded.objects:
            if obj is None:continue
            world_matrix=obj.matrix_world.copy()
            obj.parent=context_root;obj.matrix_world=world_matrix
            background.objects.link(obj)
            canonical='Background '+obj.name;obj.name='V2 '+canonical
            obj['canonical_name']=canonical;obj['review_background']=True
            obj['source_provenance']='Original MapScene continuous background mesh; copied without modifying source file'
    setup_light(scene,rig)
    unique_architecture_uvs(scene,architecture)
    front=camera(scene,rig,'V2 Full game direction',(27,-39,37),(0,0,1),43)
    reverse=camera(scene,rig,'V2 Full reverse',(22,33,26),(0,0,0),45)
    scene.camera=front
    bpy.context.view_layer.update()
    for name,matrix in anchor_snapshot.items():
        copied=anchors.objects['V2 '+name]
        if [list(row) for row in copied.matrix_world]!=matrix:raise RuntimeError('Original anchor drift: '+name)
    if [list(row) for row in portal.matrix_world]!=portal_snapshot:raise RuntimeError('Portal frame drift')
    dest=out.parent/'MapScene_Environment_Ruins_v2.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(dest))
    report={'stage':'full_geometry_promoted_after_sample_gate','gate':gate,
            'originalAnchorsUnchanged':True,'portalMatrixUnchanged':True,'portalAperture':2.09,
            'nodeCount':len(anchors.objects),'routeCount':len(routes),'sourceScenePreserved':source.name,
            'meshCount':sum(o.type=='MESH' for o in scene.objects),
            'backgroundObjects':[obj.get('canonical_name',obj.name) for obj in background.objects]}
    (out/'full-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    background.hide_render=True
    render(scene,front,out/'10_V2_Full_Clay.png',clay)
    render(scene,reverse,out/'11_V2_Reverse_Clay.png',clay)
    background.hide_render=False
    render(scene,front,out/'12_V2_Full_Materials.png')


if mode == 'sample':
    study=bpy.data.scenes.new('V2 Representative Study')
    bpy.context.window.scene=study
    geo=collection('V2 Study editable assets',study);rig=collection('V2 Study review rig',study)
    ground=rock_patch(geo,'V2 Study cliff and ground')
    platform(geo,'V2 Study embedded court',(0,-.4,-.125),9)
    path(geo,'V2 Study approach paving',(-5,-.4,0),(0,-.4,0),12)
    column(geo,'V2 Study intact order',(-2.6,1.3,-.12),5.4,False,17)
    column(geo,'V2 Study fractured order',(1.05,1.5,-.12),2.5,True,17)
    palm(geo,'V2 Study mature palm',(3.4,.1,-.12),1,27,4.45)
    fern(geo,'V2 Study root fern A',(2.95,-.30,-.1),22,1.0)
    fern(geo,'V2 Study root fern B',(3.65,.30,-.13),27,.86)
    fern(geo,'V2 Study column fern',(-2.7,.6,-.1),32,.95)
    for obj in list(geo.objects):
        if obj.name.endswith(('matching fallen shaft','continuous trunk')):
            seat_on_ground(obj,ground,.085 if 'fallen' in obj.name else .075)
    # All vegetation parts share the same root translation; no detached crown after seating.
    trunk=geo.objects['V2 Study mature palm continuous trunk']
    for obj in list(geo.objects):
        if obj.name.startswith('V2 Study mature palm') and obj!=trunk:obj.location.z+=trunk.location.z
    setup_light(study,rig)
    cams=[camera(study,rig,'V2 Study three quarter',(12,-19,12),(0,.3,1.3),46),
          camera(study,rig,'V2 Study reverse',(-10,13,8),(0,.4,1.4),49),
          camera(study,rig,'V2 Study plant close',(7,-7,5),(3,.2,3.8),48),
          camera(study,rig,'V2 Study fracture close',(-.2,-4.3,6.4),(1.20,1.65,1.7),52)]
    study.camera=cams[0]
    # A separate portal specimen preserves the actual portal matrix in the main scene.
    portal_scene=bpy.data.scenes.new('V2 Portal Connection Study')
    pg=collection('V2 Portal study geometry',portal_scene);pr=collection('V2 Portal study rig',portal_scene)
    m=Matrix.Translation(Vector((0,0,3.0))) @ Matrix.Rotation(math.pi/2,4,'X')
    portal_frame(pg,'V2 Study integral portal frame',m)
    footing=Mesh();prism(footing,[(-3.55,-1.12),(-3.16,-1.58),(2.91,-1.48),(3.48,-.95),(3.40,1.02),(-3.32,.98)],-.30,.38)
    footing.object('V2 Study portal footing',pg,[stone],.045)
    approach=Mesh()
    for i in range(3):
        width=2.4-i*.08;y=-2.58+i*.38;z=-.20+i*.18
        prism(approach,[(-width,y),(width-.12,y-.025),(width,y+.4),(-width-.04,y+.41)],-.42,z)
    approach.object('V2 Study continuous approach steps',pg,[stone],.025)
    setup_light(portal_scene,pr)
    pc=camera(portal_scene,pr,'V2 Portal structure',(7,-14,7.4),(0,0,2.5),43)
    pb=camera(portal_scene,pr,'V2 Portal reverse',(-6,12,5.5),(0,0,2.5),43)
    portal_scene.camera=pc
    bpy.context.window.scene=study
    dest=out.parent/'MapScene_Environment_Ruins_v2.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(dest))
    report=dict(stage='representative_sample', source=source_path, output=str(dest),
                originalAnchors=anchor_snapshot, originalPortalMatrix=portal_snapshot,
                sampleGate='Pending neutral, reverse, material, game-camera and real-UI inspection',
                sampleObjects=[o.name for o in geo.objects], sourceScenePreserved=source.name,
                revision='Geometry review 2: grounded fracture pair, courtyard slabs, broad rock planes, connected narrow palm blades',
                originalApertureRadius=2.09)
    (out/'sample-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    render(study,cams[0],out/'01_Sample_Clay.png',clay)
    render(study,cams[1],out/'02_Sample_Reverse_Clay.png',clay)
    render(study,cams[0],out/'03_Sample_Materials.png')
    render(study,cams[2],out/'04_Palm_Connections.png')
    render(study,cams[3],out/'05_Fracture_Volume.png')
    render(portal_scene,pc,out/'06_Portal_Integral_Frame.png',clay)
    render(portal_scene,pb,out/'07_Portal_Reverse_Clay.png',clay)
elif mode=='family':
    family=bpy.data.scenes.new('V2 Crown Family Study');bpy.context.window.scene=family
    geo=collection('V2 Crown family assets',family);rig=collection('V2 Crown family rig',family)
    for variant,x in enumerate([-6.5,0,6.5]):
        palm(geo,f'V2 Crown family {variant}',(x,0,0),variant,640+variant,4.0)
    setup_light(family,rig)
    cam=camera(family,rig,'V2 Three crown silhouettes',(14,-30,15),(0,0,2.4),44)
    render(family,cam,out/'08_Three_Crown_Silhouettes.png')
elif mode!='definitions':
    build_full_environment()
