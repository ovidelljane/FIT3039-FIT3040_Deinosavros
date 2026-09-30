"""Vertex-mask-only V3 ground revision. Geometry and every scene transform are immutable."""
import bpy
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path
import numpy as np
from mathutils import Vector

out=Path(sys.argv[sys.argv.index('--')+1]).resolve()
native=Path(bpy.data.filepath)
backup=out/'MapScene_Environment_Ruins_v3_before_masks.blend'
if not backup.exists():shutil.copy2(native,backup)
scene=bpy.data.scenes['V2 Environment Review'];bpy.context.window.scene=scene;bpy.context.view_layer.update()
island=scene.objects['V2 Island continuous fractured bedrock'];mesh=island.data
positions=np.array([v.co[:] for v in mesh.vertices],dtype=np.float64)
transforms={o.name:[tuple(row) for row in o.matrix_world] for o in scene.objects}
geometry_hash=hashlib.sha256(positions.tobytes()+str([tuple(p.vertices) for p in mesh.polygons]).encode()).hexdigest()
color=mesh.color_attributes.active_color
old=np.array([d.color[:] for d in color.data])
names={o.get('canonical_name',o.name):o for o in scene.objects}
centers=[names[f'Node terrace {i:02d}'].matrix_world.translation.copy() for i in range(13)]+[Vector((14,0,1.25))]
routes=[(0,1),(0,2),(0,3),(1,4),(2,5),(3,6),(2,4),(3,5),(4,7),(5,8),(6,9),(4,8),(5,9),(7,10),(8,11),(9,12),(10,13),(11,13),(12,13)]
x,y,z=positions.T


def smooth(a,b,v):
    t=np.clip((v-a)/(b-a),0,1);return t*t*(3-2*t)


path_distance=np.full(len(x),100.0)
for ia,ib in routes:
    a,b=centers[ia],centers[ib];dx=b.x-a.x;dy=b.y-a.y
    t=np.clip(((x-a.x)*dx+(y-a.y)*dy)/(dx*dx+dy*dy),0,1)
    path_distance=np.minimum(path_distance,np.hypot(x-a.x-dx*t,y-a.y-dy*t))
court_distance=np.min(np.array([np.hypot(x-p.x,y-p.y) for p in centers[:-1]]),axis=0)
protected=(path_distance<.40)|(court_distance<1.03)
visible=(path_distance>.72)&(court_distance>1.70)&~((x>11.5)&(np.abs(y)<3.3))
visible&=~((x>-11.8)&(x<11.5)&(y>7.55)&(y<9.05))

# The field is shaped by connected drainage/root pockets. Only its boundary is irregular.
field=np.full(len(x),-10.0)
pockets=[(-14.5,4.6,3.0,3.2),(-13.2,-4.2,3.2,2.9),(-9.4,2.5,2.65,1.60),
         (-7.1,-2.7,2.8,1.5),(-4.6,3.0,2.9,1.8),(-2.5,-3.0,2.35,1.65),
         (1.4,3.0,2.6,1.8),(5.4,-2.6,3.1,1.70),(9.3,2.7,2.6,1.8),
         (14.7,-4.8,3.3,2.8),(13.5,5.1,3.8,2.3),(-5.2,8.7,7.4,1.65),
         (-5.0,-8.3,3.2,1.55),(3.6,-8.5,3.1,1.6)]
warp_x=x+.23*np.sin(y*1.10+x*.25)+.10*np.cos(x*1.85-y*.7)
warp_y=y+.25*np.sin(x*.89-y*.38)+.09*np.cos(y*2.05+x*.6)
for px,py,rx,ry in pockets:
    r=np.sqrt(((warp_x-px)/rx)**2+((warp_y-py)/ry)**2)
    field=np.maximum(field,1-r)

root_field=np.zeros(len(x))
for obj in scene.objects:
    if obj.type!='MESH':continue
    if 'continuous trunk' in obj.name or 'rooted frond axes' in obj.name:
        n=12 if 'continuous trunk' in obj.name else 6
        p=sum((obj.matrix_world@v.co for v in obj.data.vertices[:n]),Vector())/n
        root_field=np.maximum(root_field,np.exp(-((x-p.x)**2+(y-p.y)**2)/(1.8 if n==12 else 1.25)**2))
field=np.maximum(field,root_field*.80-.20)

# Exposed rock follows the real continuous shoulder, interrupted by rooted growth.
edge_distance=np.full(len(x),100.0)
for i in range(192):
    a=positions[i];b=positions[(i+1)%192];dx=b[0]-a[0];dy=b[1]-a[1]
    t=np.clip(((x-a[0])*dx+(y-a[1])*dy)/(dx*dx+dy*dy),0,1)
    edge_distance=np.minimum(edge_distance,np.hypot(x-a[0]-dx*t,y-a[1]-dy*t))
rock_break=.66+.34*smooth(-.6,.7,np.sin(x*.38-y*.18)+.45*np.cos(y*.71+x*.15))

mesh.calc_loop_triangles()
top=[t for t in mesh.loop_triangles if mesh.polygons[t.polygon_index].material_index==1]
ids=np.array([t.vertices[:] for t in top],dtype=np.int32)
tri=positions[ids]
area=np.abs((tri[:,1,0]-tri[:,0,0])*(tri[:,2,1]-tri[:,0,1])-(tri[:,1,1]-tri[:,0,1])*(tri[:,2,0]-tri[:,0,0]))*.5
visible_weight=area*np.mean(visible[ids],axis=1)


def weights(threshold,width):
    rock=.94*(1-smooth(.12,width,edge_distance))*rock_break
    rock=np.maximum(rock,.055*np.exp(-((x-7.2)**2+(y-2.0)**2)/5))
    moss=(.018+.94*smooth(threshold-.16,threshold+.16,field))*(1-.88*rock)
    soil=np.maximum(.012,1-moss-rock)
    rgb=np.stack((soil,moss,rock),axis=1);rgb/=np.sum(rgb,axis=1,keepdims=True)
    rgb[protected]=old[protected,:3]
    side=np.arange(len(x))>=192
    side&=np.arange(len(x))<1920
    rgb[side]=old[side,:3]
    return rgb


def mean_visible(rgb,channel):
    return float(np.sum(np.mean(rgb[ids,channel],axis=1)*visible_weight)/np.sum(visible_weight))


lo,hi=.4,3.0
for _ in range(25):
    width=(lo+hi)/2
    if mean_visible(weights(.20,width),2)<.15:lo=width
    else:hi=width
width=(lo+hi)/2
lo,hi=-.5,.85
for _ in range(25):
    threshold=(lo+hi)/2
    if mean_visible(weights(threshold,width),1)>.34:lo=threshold
    else:hi=threshold
threshold=(lo+hi)/2;rgb=weights(threshold,width)
for i,item in enumerate(color.data):item.color=(*rgb[i],1)
mesh.update()
after_positions=np.array([v.co[:] for v in mesh.vertices],dtype=np.float64)
after_hash=hashlib.sha256(after_positions.tobytes()+str([tuple(p.vertices) for p in mesh.polygons]).encode()).hexdigest()
if after_hash!=geometry_hash:raise RuntimeError('Mask pass changed geometry.')
if transforms!={o.name:[tuple(row) for row in o.matrix_world] for o in scene.objects}:raise RuntimeError('Mask pass changed object transforms.')
if np.max(np.abs(rgb[protected]-old[protected,:3]))>1e-6:raise RuntimeError('Protected gameplay-center weights changed.')
stats={'geometryAndLayoutUnchanged':True,'geometrySha256':geometry_hash,'protectedVertexCount':int(np.sum(protected)),
       'normalizationError':float(np.max(np.abs(np.sum(rgb,axis=1)-1))),
       'projectedTopArea':float(np.sum(area)),'visibleTopAreaEstimate':float(np.sum(visible_weight)),
       'visibleTopBlendFractions':{'soil':mean_visible(rgb,0),'moss':mean_visible(rgb,1),'rock':mean_visible(rgb,2)},
       'allTopBlendFractions':{name:float(np.sum(np.mean(rgb[ids,i],axis=1)*area)/np.sum(area)) for i,name in enumerate(['soil','moss','rock'])},
       'visibleAreaWithDominantMoss':float(np.sum(visible_weight*(np.mean(rgb[ids,1],axis=1)>.5))/np.sum(visible_weight)),
       'visibleAreaWithDominantRock':float(np.sum(visible_weight*(np.mean(rgb[ids,2],axis=1)>.5))/np.sum(visible_weight)),
       'mossThreshold':threshold,'rockShoulderWidth':width,'topDefinition':'Projected terrain triangles; visible estimate excludes paving, court circles and major sanctuary/colonnade footprints'}
(out/'ground-mask-audit.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(native))

# Evidence-only material substitutions are never saved into the production native file.
preview=bpy.data.materials.new('V3 Mask readability preview');preview.use_nodes=True
nodes=preview.node_tree.nodes;links=preview.node_tree.links
attribute=nodes.new('ShaderNodeAttribute');attribute.attribute_name='COLOR_0'
split=nodes.new('ShaderNodeSeparateXYZ');links.new(attribute.outputs['Vector'],split.inputs['Vector'])
terms=[]
for channel,tint in zip(['X','Y','Z'],[(.245,.173,.105),(.055,.16,.022),(.285,.29,.25)]):
    mult=nodes.new('ShaderNodeVectorMath');mult.operation='SCALE';mult.inputs[0].default_value=tint
    links.new(split.outputs[channel],mult.inputs['Scale']);terms.append(mult.outputs['Vector'])
add=nodes.new('ShaderNodeVectorMath');add.operation='ADD';links.new(terms[0],add.inputs[0]);links.new(terms[1],add.inputs[1])
add2=nodes.new('ShaderNodeVectorMath');add2.operation='ADD';links.new(add.outputs['Vector'],add2.inputs[0]);links.new(terms[2],add2.inputs[1])
bsdf=nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.88
links.new(add2.outputs['Vector'],bsdf.inputs['Base Color'])
for index in range(len(island.data.materials)):island.data.materials[index]=preview
for c in scene.collection.children:
    if c.name.startswith('Review Background'):c.hide_render=True
scene.camera=scene.objects['V2 Fixed game axis check'];scene.cycles.samples=24
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.filepath=str(out/'06_Ground_Mask_Readability.png');bpy.ops.render.render(write_still=True,scene=scene.name)
links.new(attribute.outputs['Color'],bsdf.inputs['Base Color'])
scene.render.filepath=str(out/'07_Ground_Mask_RGB_Debug.png');bpy.ops.render.render(write_still=True,scene=scene.name)
print(json.dumps(stats,indent=2))
