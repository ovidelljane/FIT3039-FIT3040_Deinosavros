"""Original foliage atlas, attached runtime pinnae, and isolated full-scene refinement.

Run on the V2 native file only. The source scene and source leaf geometry stay editable.
Textures are raster-baked from authored source leaf triangles, not downloaded imagery.
"""
import bpy
import bmesh
import json
import math
import random
import runpy
import sys
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args=sys.argv[sys.argv.index('--')+1:]
out=Path(args[0]).resolve()
script=Path(__file__).with_name('build_ruins_v2.py')
sys.argv=[str(script),'--',str(out),'definitions']
api=runpy.run_path(str(script))
scene=bpy.data.scenes['V2 Environment Review']
bpy.context.window.scene=scene
bpy.context.view_layer.update()
ground=scene.objects['V2 Island continuous fractured bedrock']
plants=next(c for c in scene.collection.children if c.name.startswith('V2 Environment vegetation'))
retained=next(c for c in scene.collection.children if c.name.startswith('V2 Preserved sanctuary'))
root_tree=BVHTree.FromObject(ground,bpy.context.evaluated_depsgraph_get())
centers=api['centers'];routes=api['routes']


def route_clearance(x,y):
    result=min(math.hypot(x-p.x,y-p.y)-1.75 for p in centers)
    for ia,ib in routes:
        a,b=centers[ia],centers[ib]
        d=Vector((b.x-a.x,b.y-a.y));p=Vector((x-a.x,y-a.y))
        t=max(0,min(1,p.dot(d)/d.length_squared))
        result=min(result,(p-d*t).length-1.64)
    return result


def ground_z(x,y):
    hit,_,_,_=root_tree.ray_cast(Vector((x,y,30)),Vector((0,0,-1)),60)
    return None if hit is None else hit.z


def move_crown(index,new_xy):
    prefix=f'V2 Palm {index:02d}'
    trunk=scene.objects[prefix+' continuous trunk']
    if trunk.get('review_composition_relocated'):return
    root=trunk.matrix_world @ trunk.data.vertices[0].co
    original=Vector(((-14,-6),(-11.8,6.35),(-5,-8.1),(2.4,-8.65),(12.3,6.3),(16.2,-4.6))[index])
    delta=Vector((new_xy[0]-original.x,new_xy[1]-original.y,0))
    oldz=ground_z(original.x,original.y);newz=ground_z(*new_xy)
    delta.z=newz-oldz
    for obj in plants.objects:
        if obj.name.startswith(prefix):obj.location+=delta
    trunk['review_composition_relocated']=True
    trunk['composition_reason']='Tall foreground crowns moved away from fixed-camera court centers'


# The actual Unity view exposed foreground canopy occlusion; root clearance alone is insufficient.
move_crown(0,(-16.5,-4.0))
move_crown(2,(-2.8,9.10))
move_crown(3,(5.9,9.00))

# Additional foliage is rooted in three connected ruin-edge zones, not one pot per route gap.
growth=bpy.data.collections.get('V2 Connected ruin-edge growth')
if growth is None:
    growth=api['collection']('V2 Connected ruin-edge growth',scene)
    zones=[(-15.0,4.5,2.6,3.8),(-15.0,-4.5,2.2,2.0),(0.0,8.6,11.6,1.85),(15.8,-4.9,2.05,2.45),(14.3,5.2,2.55,1.8)]
    rng=random.Random(9821)
    accepted=[]
    for index in range(1800):
        zone=zones[index%len(zones)]
        x=zone[0]+rng.uniform(-zone[2],zone[2]);y=zone[1]+rng.uniform(-zone[3],zone[3])
        if ((x-zone[0])/zone[2])**2+((y-zone[1])/zone[3])**2>1:continue
        scale=rng.uniform(.72,1.12)
        radius=1.30*scale
        if route_clearance(x,y)<radius:continue
        if any(math.hypot(x-p[0],y-p[1])<.73*(radius+p[2]) for p in accepted):continue
        z=ground_z(x,y)
        if z is None or z<-.8:continue
        accepted.append((x,y,radius))
        api['rooted_fern'](growth,f'V2 Connected grove fern {len(accepted):02d}',(x,y,z),ground,9900+index,scale)
        if len(accepted)%5==0:
            api['broadleaf_clump'](growth,f'V2 Connected grove broadleaf {len(accepted):02d}',(x,y,z),12000+index,.72)
        if len(accepted)>=43:break
    growth['root_sites']=json.dumps(accepted)
    growth['route_clearance_rule']='Maximum frond radius plus 1.0 m from paving edge; 1.75 m court centers'

for index,(x,y,radius) in enumerate(json.loads(growth['root_sites'])):
    name=f'V2 Connected low broadleaf patch {index:02d}'
    if index%2==0 and not any(o.name.startswith(name) for o in growth.objects):
        api['broadleaf_clump'](growth,name,(x,y,ground_z(x,y)),13200+index,.90)

# Replace only the inherited review rim copy; its bevel generated tiny degenerate triangles.
rim=next(o for o in retained.objects if o.get('canonical_name')=='Portal - recessed luminous inner rim')
if not rim.get('clean_runtime_rim'):
    original=api['source'].objects['Portal - recessed luminous inner rim']
    bounds=[Vector(c) for c in original.bound_box]
    dimensions=[max(p[i] for p in bounds)-min(p[i] for p in bounds) for i in range(3)]
    major=(max(dimensions)*.5)-.013
    offset=Vector(tuple((min(p[i] for p in bounds)+max(p[i] for p in bounds))*.5 for i in range(3)))
    verts=[];faces=[]
    for i in range(192):
        a=math.tau*i/192
        for j in range(8):
            b=math.tau*j/8;r=major+.013*math.cos(b)
            verts.append(offset+Vector((r*math.cos(a),r*math.sin(a),.013*math.sin(b))))
    for i in range(192):
        for j in range(8):faces.append((i*8+j,((i+1)%192)*8+j,((i+1)%192)*8+(j+1)%8,i*8+(j+1)%8))
    mesh=bpy.data.meshes.new('V2 Clean continuous inner energy rim geometry');mesh.from_pydata(verts,[],faces);mesh.update()
    for mat in original.data.materials:mesh.materials.append(mat)
    for face in mesh.polygons:face.use_smooth=True
    rim.data=mesh;rim.modifiers.clear();rim['clean_runtime_rim']=True;rim['original_geometry']=True

atlas_dir=out/'Original_Foliage_Atlas';atlas_dir.mkdir(parents=True,exist_ok=True)
source_leaves=[]
for obj in list(scene.objects):
    if obj.type!='MESH' or obj.get('runtime_foliage'):continue
    if obj.name.endswith('attached broad pinnae') or obj.name.endswith(' pinnae'):
        source_leaves.append(obj)
if not source_leaves:raise RuntimeError('No authored source leaves found.')
sample=next(obj for obj in source_leaves if obj.name.startswith('V2 Palm 01'))
fern=next(obj for obj in source_leaves if obj.name.endswith(' pinnae') and not obj.name.endswith('broad pinnae'))
resolution=2048
colors=np.zeros((resolution,resolution,4),dtype=np.float32)
normals=np.zeros_like(colors);normals[:,:,:3]=(.5,.5,1)
roughness=np.ones_like(colors);roughness[:,:,3]=0
cells=[('mature_palm',0,0,0),('new_palm',1,0,1),('old_palm',0,1,2),('fern',1,1,0)]


def raster_triangle(uv,normal,color,rough):
    p=np.asarray(uv)*resolution
    x0=max(0,int(np.floor(p[:,0].min())));x1=min(resolution,int(np.ceil(p[:,0].max()))+1)
    y0=max(0,int(np.floor(p[:,1].min())));y1=min(resolution,int(np.ceil(p[:,1].max()))+1)
    xx,yy=np.meshgrid(np.arange(x0,x1)+.5,np.arange(y0,y1)+.5)
    denom=(p[1,1]-p[2,1])*(p[0,0]-p[2,0])+(p[2,0]-p[1,0])*(p[0,1]-p[2,1])
    if abs(denom)<1e-8:return
    a=((p[1,1]-p[2,1])*(xx-p[2,0])+(p[2,0]-p[1,0])*(yy-p[2,1]))/denom
    b=((p[2,1]-p[0,1])*(xx-p[2,0])+(p[0,0]-p[2,0])*(yy-p[2,1]))/denom
    c=1-a-b;mask=(a>=0)&(b>=0)&(c>=0)
    weights=np.stack((a,b,c),axis=-1)
    nn=weights @ np.asarray(normal)
    nn/=np.maximum(1e-7,np.linalg.norm(nn,axis=-1,keepdims=True))
    cc=weights @ np.asarray(color)
    target=colors[y0:y1,x0:x1];target[mask,:3]=cc[mask];target[mask,3]=1
    target=normals[y0:y1,x0:x1];target[mask,:3]=nn[mask]*.5+.5;target[mask,3]=1
    target=roughness[y0:y1,x0:x1];target[mask,:3]=rough;target[mask,3]=1


atlas_source=bpy.data.scenes.get('V2 Original Leaf Atlas Source')
if atlas_source is None:atlas_source=bpy.data.scenes.new('V2 Original Leaf Atlas Source')
if not atlas_source.objects:
    atlas_collection=api['collection']('V2 Editable atlas source leaf specimens',atlas_source)
else:atlas_collection=atlas_source.collection.children[0]

for label,cx,cy,material_index in cells:
    if label=='fern':
        obj=fern;start=0;stride=5
        local_uv=[(.5,.065),(.125,.5),(.5,.935),(.875,.5),(.5,.5)]
        poly=[(q,(q+1)%4,4) for q in range(4)]
        root=obj.data.vertices[0].co;tip=obj.data.vertices[2].co
        across=(obj.data.vertices[1].co-obj.data.vertices[3].co).normalized()
    else:
        obj=sample;stride=27
        face_index=next(p.index for p in obj.data.polygons if p.material_index==material_index)
        start=(min(obj.data.polygons[face_index].vertices)//stride)*stride
        coords=[obj.data.vertices[start+i].co for i in range(stride)]
        maximum=max((coords[r*3+2]-coords[r*3]).length*.5 for r in range(9))
        local_uv=[]
        for row in range(9):
            width=(coords[row*3+2]-coords[row*3]).length*.5/maximum*.375
            local_uv.extend([(.5-width,.065+.87*row/8),(.5,.065+.87*row/8),(.5+width,.065+.87*row/8)])
        poly=[]
        for row in range(8):
            for col in range(2):
                a=row*3+col;poly.extend([(a,a+1,a+4),(a,a+4,a+3)])
        root=coords[1];tip=coords[-2];across=(coords[2]-coords[0]).normalized()
    along=(tip-root).normalized();face_normal=across.cross(along).normalized()
    mat=obj.data.materials[material_index]
    base=np.asarray(mat.diffuse_color[:3])
    transformed=[];vertex_colors=[]
    for i,(u,v) in enumerate(local_uv):
        nn=obj.data.vertices[start+i].normal
        tangent=np.asarray([nn.dot(across),nn.dot(along),abs(nn.dot(face_normal))])
        tangent[:2]*=.35;tangent[2]=max(.5,tangent[2]);tangent/=np.linalg.norm(tangent)
        transformed.append(tangent)
        vertex_colors.append(base*(.95+.10*v+.035*math.cos(u*math.pi)))
    uv=[((cx+u)*.5,(cy+v)*.5) for u,v in local_uv]
    for tri in poly:raster_triangle([uv[i] for i in tri],[transformed[i] for i in tri],[vertex_colors[i] for i in tri],mat.roughness)
    name='V2 Atlas source '+label
    if not atlas_source.objects.get(name):
        mesh=bpy.data.meshes.new(name+' geometry')
        mesh.from_pydata([(u*4,v*4,.02*(1-abs(local_uv[i][0]-.5)*2)) for i,(u,v) in enumerate(uv)],[],poly)
        mesh.materials.append(mat);mesh.update()
        specimen=bpy.data.objects.new(name,mesh);atlas_collection.objects.link(specimen)
        specimen['derived_from']=obj.name;specimen['source_vertex_start']=start


def save_map(name,data,linear):
    # Extend RGB beyond the clip silhouette for clean mip filtering; alpha stays untouched.
    mask=data[:,:,3]>.5
    for iteration in range(16):
        incoming=np.zeros_like(data[:,:,:3]);count=np.zeros(mask.shape,dtype=np.float32)
        for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)]:
            neighbor=np.roll(mask,(dy,dx),(0,1))
            incoming+=np.roll(data[:,:,:3],(dy,dx),(0,1))*neighbor[:,:,None]
            count+=neighbor
        fill=(~mask)&(count>0)
        data[fill,:3]=incoming[fill]/count[fill,None];mask|=fill
    img=bpy.data.images.get(name) or bpy.data.images.new(name,width=resolution,height=resolution,alpha=True)
    img.colorspace_settings.name='Non-Color' if linear else 'sRGB'
    if not linear:
        # Image.save writes generated byte-image values directly; encode linear source colors.
        rgb=data[:,:,:3]
        data[:,:,:3]=np.where(rgb<=.0031308,rgb*12.92,1.055*np.maximum(rgb,0)**(1/2.4)-.055)
    img.pixels.foreach_set(data.ravel());img.update()
    img.file_format='PNG';img.filepath_raw=str(atlas_dir/(name+'.png'));img.save()
    img.pack()
    return img


base_image=save_map('RuinsV2_Leaf_BaseColor',colors,False)
normal_image=save_map('RuinsV2_Leaf_Normal',normals,True)
rough_image=save_map('RuinsV2_Leaf_Roughness',roughness,True)
material=bpy.data.materials.get('V2 Runtime Original Leaf Atlas') or bpy.data.materials.new('V2 Runtime Original Leaf Atlas')
material.use_nodes=True;material.diffuse_color=(.11,.22,.035,1);material.roughness=.82
nodes=material.node_tree.nodes;links=material.node_tree.links;nodes.clear()
output=nodes.new('ShaderNodeOutputMaterial');bsdf=nodes.new('ShaderNodeBsdfPrincipled')
links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
bsdf.inputs['Metallic'].default_value=0;bsdf.inputs['Specular IOR Level'].default_value=.18
bsdf.inputs['Sheen Weight'].default_value=0
for label,img,socket in [('Original BaseColor',base_image,'Base Color'),('Original Roughness',rough_image,'Roughness')]:
    tex=nodes.new('ShaderNodeTexImage');tex.image=img;tex.label=label
    links.new(tex.outputs['Color'],bsdf.inputs[socket])
    if socket=='Base Color':
        clip=nodes.new('ShaderNodeMath');clip.operation='GREATER_THAN';clip.inputs[1].default_value=.35
        links.new(tex.outputs['Alpha'],clip.inputs[0]);links.new(clip.outputs[0],bsdf.inputs['Alpha'])
tex=nodes.new('ShaderNodeTexImage');tex.image=normal_image;tex.label='Original Tangent Normal'
normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.45
links.new(tex.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
material['original_atlas']=True;material['alpha_clip']=.35
material['base_map']=base_image.filepath_raw;material['normal_map']=normal_image.filepath_raw;material['roughness_map']=rough_image.filepath_raw
runtime=bpy.data.collections.get('V2 Runtime Curved Leaf Cards') or api['collection']('V2 Runtime Curved Leaf Cards',scene)
archive=bpy.data.collections.get('V2 Editable High Detail Leaf Sources') or api['collection']('V2 Editable High Detail Leaf Sources',scene)
archive.hide_render=True;archive.hide_viewport=True
report=[]
for obj in source_leaves:
    name=obj.name+' runtime cards'
    if scene.objects.get(name):continue
    is_palm=obj.name.endswith('attached broad pinnae');stride=27 if is_palm else 5
    verts=[];faces=[];uvs=[]
    for start in range(0,len(obj.data.vertices),stride):
        source_coords=[obj.data.vertices[start+i].co.copy() for i in range(stride)]
        if is_palm:
            rows=[0,3,5,8]
            maximum=max((source_coords[r*3+2]-source_coords[r*3]).length*.5 for r in range(9))
            mi=obj.data.polygons[(start//stride)*16].material_index
            cx,cy=[(0,0),(1,0),(0,1)][mi]
            points=[((source_coords[r*3]+source_coords[r*3+2])*.5,(source_coords[r*3+2]-source_coords[r*3]).normalized(),r/8) for r in rows]
        else:
            root=source_coords[0];tip=source_coords[2];across=(source_coords[1]-source_coords[3]).normalized()
            maximum=(source_coords[1]-source_coords[3]).length*.5
            cx,cy=1,1
            points=[(root.lerp(tip,t)+Vector((0,0,.016*math.sin(math.pi*t))),across,t) for t in [0,1/3,2/3,1]]
        offset=len(verts)
        for center,across,t in points:
            verts.extend([center-across*maximum,center+across*maximum])
            uvs.extend([((cx+.125)*.5,(cy+.067+.866*t)*.5),((cx+.875)*.5,(cy+.067+.866*t)*.5)])
        for row in range(3):
            a=offset+row*2;faces.append((a,a+1,a+3,a+2))
    mesh=bpy.data.meshes.new(name+' geometry');mesh.from_pydata(verts,[],faces);mesh.materials.append(material);mesh.update()
    uv_layer=mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:uv_layer.data[loop_index].uv=uvs[mesh.loops[loop_index].vertex_index]
        poly.use_smooth=True
    card=bpy.data.objects.new(name,mesh);runtime.objects.link(card);card.matrix_world=obj.matrix_world.copy()
    card['runtime_foliage']=True;card['intentional_open_surface']=True;card['original_geometry']=True
    card['source_leaf_object']=obj.name;card['leaflet_count']=len(obj.data.vertices)//stride
    for old in list(obj.users_collection):old.objects.unlink(obj)
    archive.objects.link(obj);obj['editable_high_detail_source']=True
    report.append({'source':obj.name,'runtime':name,'leaflets':card['leaflet_count'],'runtimeTriangles':len(faces)*2})

# Two-pixel inset gives clipped roots opaque texels even at a bilinear sample boundary.
for card in runtime.objects:
    if card.get('root_uv_inset_verified'):continue
    if card.name not in [item['runtime'] for item in report]:
        for loop in card.data.uv_layers.active.data:
            cell_y=math.floor(loop.uv.y*2);local_y=loop.uv.y*2-cell_y
            t=(local_y-.065)/.87
            loop.uv.y=(cell_y+.067+.866*t)*.5
    card['root_uv_inset_verified']=True

bpy.context.window.scene=scene;bpy.context.view_layer.update()
scene.camera=scene.objects['V2 Full game direction']
rig=next(c for c in scene.collection.children if c.name.startswith('V2 Environment review rig'))
game_camera=scene.objects.get('V2 Fixed game axis check') or api['camera'](scene,rig,'V2 Fixed game axis check',(0,-40,48),(0,0,0),32)
leaf_camera=scene.objects.get('V2 Runtime leaflet connection check') or api['camera'](scene,rig,'V2 Runtime leaflet connection check',(-8,1,7),(-11.3,6.35,5.6),55)
output=out.parent/'MapScene_Environment_Ruins_v2.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(output))
runtime_entries=[{'runtime':obj.name,'source':obj.get('source_leaf_object',''),'leaflets':obj.get('leaflet_count',0),
                  'runtimeTriangles':len(obj.data.polygons)*2} for obj in runtime.objects if obj.type=='MESH']
summary={'originalPixels':True,'source':'Raster bake of original authored leaf triangles and material colors',
         'resolution':resolution,'material':material.name,'maps':{'base':base_image.filepath_raw,'normal':normal_image.filepath_raw,'roughness':rough_image.filepath_raw},
         'uvCells':cells,'cardQuadsPerPinna':3,'editableSourceCollection':archive.name,'runtimeCollection':runtime.name,
         'runtimeObjects':runtime_entries,'runtimeTriangles':sum(item['runtimeTriangles'] for item in runtime_entries),
         'connectedRootSites':json.loads(growth['root_sites']),
         'rimDegeneratesRemoved':True,'visualAcceptance':'Pending updated native and actual Unity camera review'}
(out/'runtime-foliage-manifest.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
background=next(c for c in scene.collection.children if c.name.startswith('Review Background'))
background.hide_render=True
api['render'](scene,scene.objects['V2 Full game direction'],out/'13_V2_Growth_And_Runtime_Leaves.png')
api['render'](scene,scene.objects['V2 Full reverse'],out/'14_V2_Growth_Reverse.png')
api['render'](scene,leaf_camera,out/'15_V2_Clipped_Leaf_Attachments.png')
api['render'](scene,game_camera,out/'16_V2_Fixed_Game_Axis.png')
print(json.dumps({'runtimeLeafObjects':len(report),'newRootSites':len(json.loads(growth['root_sites'])),'saved':str(output)}))
