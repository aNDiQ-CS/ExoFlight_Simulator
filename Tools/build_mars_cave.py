"""Build the MarsMission cave as one signed surface. Input: Unity heights in metres, z-major float32."""
import sys, struct
from pathlib import Path
import numpy as np
from scipy.ndimage import gaussian_filter, map_coordinates
from scipy.interpolate import PchipInterpolator
from scipy.spatial import cKDTree
H=np.fromfile(sys.argv[1],'<f4').reshape(513,513); Hs=gaussian_filter(H,2.0)
# Terrain cell ranges, exclusive at upper bound. Must match CaveMouthBindings.cs.
pads=[(257,295,305,339),(313,351,446,485)]
dx,dz=930/512,1000/512
# Centre of the original passage and station chamber, measured in world metres.
route=np.array([[498,47,591],[499,46,614],[500,46,639],[499.5,45,660],[502.5,38.5,685],[511.5,27.5,708],[530.5,17.5,730],[556.5,10,750],[576.5,4,775],[581.5,3,797],[579.5,6,819],[576.5,7,835],[578,9,852],[579.5,10,870],[586,15,885],[600,23,900],[614,31,915],[623,34,922],[635,36,929],[648,37,935]])
s=np.r_[0,np.cumsum(np.linalg.norm(np.diff(route[:,[0,2]],axis=0),axis=1))]
sp=PchipInterpolator(s,route,axis=0);ss=np.linspace(0,s[-1],int(s[-1]*6));path=sp(ss);tree=cKDTree(path[:,[0,2]])
def smooth(x):x=np.clip(x,0,1);return x*x*(3-2*x)
# Grid coincides with the Terrain grid at every second vertex.
xs=np.arange(256,354)*dx;zs=np.arange(301,487)*dz;dy=1.23;ys=np.arange(-21.37,113.,dy)
Z,X=np.meshgrid(zs,xs,indexing='ij');pos=np.stack([X.ravel(),Z.ravel()],axis=1)
_,ix=tree.query(pos);pp=path[ix];along=ss[ix].reshape(Z.shape);tan=sp.derivative()(ss[ix])[:,[0,2]];tan/=np.linalg.norm(tan,axis=1)[:,None]
lateral=((pos-pp[:,[0,2]])*tan[:,::-1]*[1,-1]).sum(1).reshape(Z.shape);cy=pp[:,1].reshape(Z.shape)
extra=np.maximum.reduce([-((pos-path[0,[0,2]])*tan[0]).sum(1)* (ix==0),((pos-path[-1,[0,2]])*tan[-1]).sum(1)*(ix==len(ss)-1),np.zeros(len(ix))]).reshape(Z.shape)
# Use the original elevation on the perimeter, smoothing only the old sculpted mouth.
h=map_coordinates(H,[Z/dz,X/dx],order=1);hs=map_coordinates(Hs,[Z/dz,X/dx],order=1)
patch=np.zeros(h.shape,bool);fac=np.zeros(h.shape)
for l,r,b,t in pads:
 dist=np.minimum.reduce([X/dx-l,r-X/dx,Z/dz-b,t-Z/dz]);patch|=dist>=0;fac=np.maximum(fac,smooth(dist/7))
h=h+(hs-h)*fac
chamber=np.exp(-((Z-849)/23)**2)
width=12.7+5.5*chamber+1.2*np.sin(along*.039);height=8.3+3*chamber+.72*np.sin(along*.061)
Y=ys[None,None,:]; lateral=lateral[:,:,None]; hh=(Y-cy[:,:,None])/height[:,:,None]
ang=np.arctan2(hh,lateral/width[:,:,None]);S=along[:,:,None]
shape=1+.059*np.sin(3*ang+.028*S)+.035*np.cos(5*ang-.036*S)+.023*np.sin(.20*S+2*ang)
tube=((np.sqrt((lateral/width[:,:,None])**2+hh**2+(extra[:,:,None]/8)**2)-shape)*height[:,:,None]).astype('f4')
ground=(h[:,:,None]-Y).astype('f4')
# Positive = solid. Smooth intersection rolls the lips into the hillside rather than capping a pipe.
K=4.8;F=(np.minimum(ground,tube)-np.maximum(K-np.abs(ground-tube),0)**2/(4*K)).astype('f4')
g=np.gradient(F,dz,dx,dy)
# weight for original Terrain textures on the surroundings
blend=smooth((tube-ground+K)/(2*K))
lo=F[:-1,:-1,:-1]>0;active=np.zeros(lo.shape,bool)
corners=np.array([[0,0,0],[0,1,0],[1,1,0],[1,0,0],[0,0,1],[0,1,1],[1,1,1],[1,0,1]])
for z,x,y in corners:active|=(F[z:z+lo.shape[0],x:x+lo.shape[1],y:y+lo.shape[2]]>0)!=lo
Zc=(Z[:-1,:-1]+dz/2)/dz;Xc=(X[:-1,:-1]+dx/2)/dx;ppatch=np.zeros(Zc.shape,bool)
for l,r,b,t in pads:ppatch|=(Xc>=l)&(Xc<r)&(Zc>=b)&(Zc<t)
active&=ppatch[:,:,None]|((ground[:-1,:-1,:-1]-tube[:-1,:-1,:-1])>0)
cells=np.argwhere(active); allv=[];alln=[];allw=[];faces=[]
def interp(A,B):
 a=cells_sel+corners[A];b=cells_sel+corners[B];ia=tuple(a.T);ib=tuple(b.T);t=F[ia]/(F[ia]-F[ib]);q=a+(b-a)*t[:,None];v=np.stack([xs[0]+q[:,1]*dx,ys[0]+q[:,2]*dy,zs[0]+q[:,0]*dz],axis=1)
 n=-np.stack([G[ia]+t*(G[ib]-G[ia]) for G in [g[1],g[2],g[0]]],axis=1);n/=np.maximum(.00001,np.linalg.norm(n,axis=1)[:,None]);return v,n,blend[ia]+t*(blend[ib]-blend[ia])
for tet in [[0,1,2,6],[0,2,3,6],[0,3,7,6],[0,7,4,6],[0,4,5,6],[0,5,1,6]]:
 code=sum((F[tuple((cells+corners[k]).T)]>0).astype(int)*(1<<i) for i,k in enumerate(tet))
 for key in range(1,15):
  cells_sel=cells[code==key]
  if not len(cells_sel):continue
  a=[k for i,k in enumerate(tet) if key&(1<<i)];b=[k for k in tet if k not in a]
  if len(a)>len(b):a,b=b,a
  pairs=[(i,j) for i in a for j in b];vals=[interp(i,j) for i,j in pairs]
  for ids in ([[0,1,2],[1,3,2]] if len(vals)==4 else [[0,1,2]]):
   v,n,w=[np.stack([vals[i][k] for i in ids],axis=1) for k in range(3)]
   bad=np.sum(np.cross(v[:,1]-v[:,0],v[:,2]-v[:,0])*n.mean(1),axis=1)<0
   v[bad]=v[bad,::-1];n[bad]=n[bad,::-1];w[bad]=w[bad,::-1];allv.append(v.reshape(-1,3));alln.append(n.reshape(-1,3));allw.append(w.ravel())
v=np.concatenate(allv);n=np.concatenate(alln);w=np.concatenate(allw);_,first,inv=np.unique(np.round(v,5),axis=0,return_index=True,return_inverse=True)
f=inv.reshape(-1,3);f=f[(f[:,0]!=f[:,1])&(f[:,1]!=f[:,2])&(f[:,2]!=f[:,0])]
with open(sys.argv[2],'wb') as o:
 o.write(struct.pack('<ii',len(first),f.size))
 for buf in (v[first],n[first],w[first]):o.write(buf.astype('<f4').tobytes())
 o.write(f.astype('<i4').tobytes())
print(len(first),'vertices',len(f),'triangles')


