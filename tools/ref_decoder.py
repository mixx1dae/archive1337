"""Незалежний еталонний декодер архіву SARC (Python) для звірки з C#-реалізацією.

Читає томи, перевіряє HMAC, розшифровує навантаження, розбирає таблиці і розпаковує
блоки (канонічний Гаффман + LZ77) за FORMAT_SPEC_P2.md. Корисний П1 для налагодження Гаффмана:
на ref_01, ref_03, ref_04, ref_06 можна порівняти довжини кодів і потік токенів.

Потрібен пакет `cryptography`:  pip install cryptography
Приклад:
    python tools/ref_decoder.py algo2026 reference/ref_01_single_file.arc
"""
import struct, zlib, hashlib, hmac, json
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

class Err(Exception):
    def __init__(s,c,m=""): super().__init__(c+": "+m); s.code=c

def ctr(key, nonce, data, first=1):
    enc = Cipher(algorithms.AES(key), modes.ECB()).encryptor()
    out=bytearray(); i=first
    for off in range(0,len(data),16):
        ks=enc.update(nonce+struct.pack('>I',i)); out+=bytes(a^b for a,b in zip(data[off:off+16],ks)); i+=1
    return bytes(out)

def canon(lengths):
    bl=[0]*17
    for L in lengths.values(): bl[L]+=1
    bl[0]=0; code=0; nxt=[0]*17
    for L in range(1,16):
        code=(code+bl[L-1])<<1; nxt[L]=code
    out={}
    for s in sorted(lengths):
        L=lengths[s]; out[(nxt[L],L)]=s; nxt[L]+=1
    return out

def read_table(b,p):
    n,=struct.unpack_from('>H',b,p); p+=2; d={}
    for _ in range(n):
        s,L=struct.unpack_from('>HB',b,p); p+=3
        if L<1 or L>15: raise Err("E_CODE","len")
        d[s]=L
    return d,p

def decode_block(coded, raw_len):
    p=0
    lit,p=read_table(coded,p); ln,p=read_table(coded,p); ds,p=read_table(coded,p)
    tc,sl=struct.unpack_from('>II',coded,p); p+=8
    stream=coded[p:p+sl]
    if p+sl!=len(coded): raise Err("E_PAYLOAD","block len")
    bits=''.join(format(x,'08b') for x in stream); pos=0
    tabs=[canon(t) for t in (lit,ln,ds)]
    def sym(tab):
        nonlocal pos
        cur=0; L=0
        while True:
            if pos>=len(bits): raise Err("E_CODE","eos")
            cur=(cur<<1)|int(bits[pos]); pos+=1; L+=1
            if (cur,L) in tab: return tab[(cur,L)]
            if L>15: raise Err("E_CODE","badcode")
    out=bytearray()
    for _ in range(tc):
        s=sym(tabs[0])
        if s<256: out.append(s)
        else:
            L=sym(tabs[1])+3
            c=sym(tabs[2])
            if c>15: raise Err("E_CODE","dist16")
            ex=max(0,c-1)
            e=int(bits[pos:pos+ex],2) if ex else 0; pos+=ex
            dist=(1 if c==0 else 2 if c==1 else ((1<<(c-1))|e)+1)
            if dist>len(out): raise Err("E_CODE","far")
            for _ in range(L): out.append(out[-dist])
    if set(bits[pos:])-{'0'}: raise Err("E_CODE","pad")
    if len(out)!=raw_len: raise Err("E_PAYLOAD","rawlen %d %d"%(len(out),raw_len))
    return bytes(out), dict(lit=lit,len=ln,dist=ds,tokens=tc,sl=sl)

def parse_volume(v, password):
    if len(v)<73: raise Err("E_SHORT")
    if v[:4]!=b"SARC" or v[4]!=1: raise Err("E_MAGIC")
    idx,cnt=struct.unpack_from('>HH',v,5); salt=v[9:25]; nonce=v[25:37]; cl,=struct.unpack_from('>I',v,37)
    if len(v)!=41+cl+32: raise Err("E_LENGTH")
    master=hashlib.pbkdf2_hmac('sha256',password.encode(),salt,100000,64); ke,km=master[:32],master[32:]
    if not hmac.compare_digest(hmac.new(km,v[:41+cl],hashlib.sha256).digest(), v[41+cl:]): raise Err("E_HMAC")
    pl=ctr(ke,nonce,v[41:41+cl])
    p=0; fc,fl,res=struct.unpack_from('>HBB',pl,p); p+=4
    if res!=0 or fl&0xFE: raise Err("E_PAYLOAD","res/flags")
    files=[]
    for _ in range(fc):
        nl,=struct.unpack_from('>H',pl,p); p+=2; name=pl[p:p+nl].decode('utf-8'); p+=nl
        osz,crc,ft,st=struct.unpack_from('>IIBB',pl,p); p+=10
        if ft>6: raise Err("E_PAYLOAD","ft")
        files.append(dict(name=name,size=osz,crc=crc,type=ft,strategy=st))
    bc,=struct.unpack_from('>H',pl,p); p+=2; blocks=[]
    for _ in range(bc):
        fi,seq,rl,cl2,bf=struct.unpack_from('>HIIIB',pl,p); p+=15
        blocks.append(dict(file=fi,seq=seq,raw=rl,coded=cl2,flags=bf))
    for b in blocks:
        b['data']=pl[p:p+b['coded']]; p+=b['coded']
    if p!=len(pl): raise Err("E_PAYLOAD","extra %d %d"%(p,len(pl)))
    return dict(idx=idx,cnt=cnt,files=files,blocks=blocks,salt=salt,nonce=nonce,flags=fl)

def extract(vols, password):
    parsed=[parse_volume(v,password) for v in vols]
    acc={}
    for pv in parsed:
        for f in pv['files']: acc.setdefault(f['name'],(f,{}))
        for b in pv['blocks']:
            f=pv['files'][b['file']]
            raw = b['data'] if b['flags']&1 else decode_block(b['data'],b['raw'])[0]
            acc[f['name']][1][b['seq']]=raw
    res={}
    for n,(f,bl) in acc.items():
        data=b''.join(bl[k] for k in sorted(bl))
        if len(data)!=f['size']: raise Err("E_PAYLOAD","size")
        if zlib.crc32(data)!=f['crc']: raise Err("E_CRC",n)
        res[n]=data
    return res,parsed


if __name__ == "__main__":
    import sys
    password, paths = sys.argv[1], sys.argv[2:]
    try:
        files, parsed = extract([open(p, "rb").read() for p in paths], password)
    except Err as e:
        print("Помилка:", e)
        sys.exit(1)
    for pv in parsed:
        print(f"том {pv['idx']}/{pv['cnt']}: файлів {len(pv['files'])}, блоків {len(pv['blocks'])}")
    for name, data in files.items():
        print(f"  {name}: {len(data)} байтів, crc32 {zlib.crc32(data):08X}")
