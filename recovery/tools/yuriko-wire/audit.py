"""Manual 32-bit source-level wire audit; emits no repository or game changes."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import subprocess

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--sdk-prefix',type=Path,required=True,help='Prefix followed by -domination, -slam, -shield worktree suffixes')
parser.add_argument('--runtime-prefix',type=Path,required=True)
parser.add_argument('--out',type=Path,required=True)
parser.add_argument('--cxx',default='cl' if os.name=='nt' else 'g++')
parser.add_argument('--kind',choices=['msvc','gnu'],default='msvc' if os.name=='nt' else 'gnu')
parser.add_argument('--runner-json',default='[]',help='JSON argv prefix, e.g. qemu + loader; empty on Windows')
options=parser.parse_args()
p=options.out.resolve();p.mkdir(parents=True,exist_ok=True)
runner=json.loads(options.runner_json)
if not isinstance(runner,list) or not all(isinstance(item,str) for item in runner):parser.error('--runner-json must be a string array')

def write_source(name,source):
    source=re.sub(r'"/workspace/work/sdk-wire-audit/([^"\n]+)"',lambda match:json.dumps((p/match.group(1)).as_posix()),source)
    (p/name).write_text(source,encoding='utf-8')

common=r'''
#include <cassert>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <vector>
#include <array>
#include <fstream>
#include <iterator>
#include <iostream>
#include <string_view>
#include <cmath>
#define __cdecl
#define __fastcall
using Allocate=void*(unsigned);using Free=void(void*);
void* alloc(unsigned n){return std::calloc(1,n);}
void release(void* p){std::free(p);}
std::vector<unsigned char> read(const char* file){std::ifstream f(file,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}};}
std::uint32_t* wrapper(std::vector<unsigned char>& b,std::uint32_t type){for(unsigned i=0;i+12<=b.size();i+=4){auto* p=(std::uint32_t*)(b.data()+i);if(p[0]==2&&p[1]==type&&p[2]<b.size())return p;}std::abort();}
std::uint32_t value(unsigned char* base,std::uint32_t* table,std::uint32_t name){
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==name)return *(std::uint32_t*)(base+r[2]);
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==1&&r[2])return value(base,(std::uint32_t*)(base+r[2]),name);
 return 0;
}
'''
# Lure's actual parse function, with only allocation and hash platform seams.
r=Path(str(options.runtime_prefix.resolve())+'-domination');s=(r/'ext/YurikoDominationExt.cpp').read_text();parse=s[s.index('    void __cdecl parse(unsigned char*'):s.index('    char const* __fastcall name(Module*')]
source=common+r'''
#include "ra3defines/YurikoLure.h"
struct Position{float x,y,z;};
namespace util{std::uint32_t getCaseSensitiveHash(const char* s){return s[0]=='x'?0x043c9363:s[0]=='y'?0x1e8fda1a:0x30be6097;}}
namespace GameAddress{template<class T>T get_address(std::string_view k){return k=="allocate_game_memory"?reinterpret_cast<T>(&alloc):reinterpret_cast<T>(&release);}}
void RaiseException(unsigned,unsigned,unsigned,void*){std::abort();}
#define EXCEPTION_NONCONTINUABLE 1
using namespace YurikoLureContract;
'''+parse+r'''
int main(){auto b=read("/workspace/work/sdk-wire-audit/lure.bin");void* result{};parse(b.data(),wrapper(b,typeId),&result,nullptr);auto* d=(YurikoLureData*)result;
assert(d->typeId==typeId&&d->moduleId&&d->linkId==7&&d->maxRangeSquared==22500&&d->guardRange==99&&d->updateRate==12);
assert(d->targetMarkerObject&&d->disabledTypesToProcess==12&&d->offsetCount==3&&d->offsets);
auto* positions=(Position*)(std::uintptr_t)d->offsets;for(unsigned i=0;i<3;++i)assert(positions[i].x==i*10&&positions[i].y==20&&positions[i].z==3);
destroyData(&result,nullptr);assert(!result);std::cout<<"actualSDK->runtime Lure68 parser: PASS\n";}
'''
write_source('audit-lure.cpp',source)
# Slam prefix parser and map allocation/destruction, with in-place native seams.
r=Path(str(options.runtime_prefix.resolve())+'-slam');s=(r/'ext/YurikoSlamExt.cpp').read_text();parse=s[s.index('    void parsePrefix('):s.index('    Module* findModule(')]
source=common+r'''
#include "ra3defines/YurikoSlam.h"
using namespace YurikoSlam;
constexpr std::uint32_t nativeLiftType=0xBF91BFCD;
void* prefixDestination{};
void prefixFields(unsigned char* base,std::uint32_t* table,void* out,std::uint32_t type){
 assert(table[0]==1);auto* p=(std::uint32_t*)out;p[0]=type;p[1]=value(base,table,0x8CDC9898);prefixDestination=out;
 if(type==storeType)p[114]=value(base,table,0xFE5DBC0B);
 if(type==nativeLiftType)p[2]=value(base,table,0x38026647);
}
void storeFields(unsigned char* b,std::uint32_t* t,void* o,void*){prefixFields(b,t,o,storeType);}
void powerFields(unsigned char* b,std::uint32_t* t,void* o,void*){prefixFields(b,t,o,powerType);}
void liftFields(unsigned char* b,std::uint32_t* t,void* o,void*){prefixFields(b,t,o,nativeLiftType);}
namespace GameAddress{template<class T>T get_address(std::string_view k){
 if(k=="allocate_game_memory")return reinterpret_cast<T>(&alloc);
 if(k=="deallocate_game_memory")return reinterpret_cast<T>(&release);
 if(k=="StoreObjectsSpecialPower::ParseFields")return reinterpret_cast<T>(&storeFields);
 if(k=="SpecialPowerModule::ParseFields")return reinterpret_cast<T>(&powerFields);
 assert(k=="LiftObjectUpdate::ParseFields");return reinterpret_cast<T>(&liftFields);
}}
namespace CustomModuleRegistry{void destroyNative(void** out,void*){release(*out);*out=nullptr;}}
'''+parse+r'''
int main(){void* out{};
{auto b=read("/workspace/work/sdk-wire-audit/add-lift.bin");parseAdd(b.data(),wrapper(b,addType),&out,nullptr);auto* d=(AddData*)out;assert(out==prefixDestination&&d->nativeStore[114]==4&&d->liftUpdateLinkId==7);destroyData(&out,nullptr);}
{auto b=read("/workspace/work/sdk-wire-audit/fling.bin");parseFling(b.data(),wrapper(b,flingType),&out,nullptr);auto* d=(FlingData*)out;assert(d->storeObjectsLinkId==4&&d->liftUpdateLinkId==7&&d->maxSpeed==420&&d->minSpeed==180&&d->replacementCount==3);
auto* pairs=(ObjectReplacement*)(std::uintptr_t)d->replacementData;assert(out==prefixDestination);for(unsigned i=0;i<3;++i)assert(pairs[i].source&&pairs[i].replacement&&pairs[i].source!=pairs[i].replacement);destroyData(&out,nullptr);}
{auto b=read("/workspace/work/sdk-wire-audit/lift.bin");parseLift(b.data(),wrapper(b,0xAA9E10D6),&out,nullptr);auto* d=(std::uint32_t*)out;float velocity;std::memcpy(&velocity,d+2,4);assert(out==prefixDestination&&d[0]==0xAA9E10D6&&d[10]==7&&velocity==8);destroyLift(&out,nullptr);}
assert(!out);std::cout<<"actualSDK->runtime AddLift/Fling476/map/Lift44: PASS\n";}
'''
write_source('audit-slam.cpp',source)
# Shield uses exact parser, reference forwarder and derived field reader.
r=Path(str(options.runtime_prefix.resolve())+'-shield');s=(r/'ext/YurikoShieldExt.cpp').read_text();parse=s[s.index('    void resolveReference('):s.index('    void __cdecl destroyData(')]
source=common+r'''
#include "ra3defines/YurikoShield.h"
using namespace YurikoShieldContract;
using BaseParse=void(unsigned char*,std::uint32_t const*,void*,void*);
void baseParse(unsigned char* base,std::uint32_t const* raw,void* output,void*){auto* rows=const_cast<std::uint32_t*>(raw);assert(rows&&rows[0]==1&&rows[1]==0xF1002505);auto radius=value(base,rows,0xE28D5E3B);std::memcpy((unsigned char*)output+8,&radius,4);}
namespace GameAddress{template<class T>T get_address(std::string_view k){return k=="allocate_game_memory"?reinterpret_cast<T>(&alloc):reinterpret_cast<T>(&baseParse);}}
template<class T>T& field(void* p,unsigned offset){return *(T*)((unsigned char*)p+offset);}
void resolve(void*,void*,std::uint32_t index,void* output){*(std::uint32_t*)output=1000+index;}
'''+parse+r'''
int main(){auto b=read("/workspace/work/sdk-wire-audit/shield.bin");std::uintptr_t table[]={reinterpret_cast<std::uintptr_t>(&resolve)};struct Resolver {std::uintptr_t* vtable;} object{table};void* resolver=&object;void* tracker=&resolver;void* out{};parse(b.data(),wrapper(b,typeId),&out,tracker);
auto* d=(Data*)out;float radius;std::memcpy(&radius,d->shieldSphere+8,4);
assert(d->reflectionFX==1018&&d->alternateReflectionFX==1024&&radius==90);
assert(d->alternateDamageTypes[0]==((1u<<21)|(1u<<22))&&d->alternateDamageTypes[1]==0);release(out);
std::cout<<"actualSDK->runtime Shield280/base/ref1based/damage64: PASS\n";}
'''
write_source('audit-shield.cpp',source)

r=Path(str(options.runtime_prefix.resolve())+'-domination')
production=(r/'ext/YurikoDominationExt.cpp').read_text()
functions=production[production.index('    void __cdecl parseAdd('):production.index('    void* __fastcall createAdd(')]
write_source('audit-add-lure.cpp',r'''
#include <cassert>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <vector>
#include <array>
#include <fstream>
#include <iterator>
#include <iostream>
#include <string_view>
#include <cmath>
#define __cdecl
#define __fastcall
using Allocate=void*(unsigned);using Free=void(void*);
void* alloc(unsigned n){return std::calloc(1,n);}
void release(void* p){std::free(p);}
std::vector<unsigned char> read(const char* file){std::ifstream f(file,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}};}
std::uint32_t* wrapper(std::vector<unsigned char>& b,std::uint32_t type){for(unsigned i=0;i+12<=b.size();i+=4){auto* p=(std::uint32_t*)(b.data()+i);if(p[0]==2&&p[1]==type&&p[2]<b.size())return p;}std::abort();}
std::uint32_t value(unsigned char* base,std::uint32_t* table,std::uint32_t name){
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==name)return *(std::uint32_t*)(base+r[2]);
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==1&&r[2])return value(base,(std::uint32_t*)(base+r[2]),name);
 return 0;
}


#include "ra3defines/YurikoLure.h"
using namespace YurikoLureContract;
namespace CustomModuleRegistry{using Parse=void(unsigned char*,std::uint32_t*,void**,void*);using Destroy=void(void**,void*);}
template<class T>T& field(void* p,unsigned offset){return *(T*)((unsigned char*)p+offset);}
void* storeDestination{};
void nativeFields(unsigned char* base,std::uint32_t* body,void* out,void*){assert(body[0]==1&&body[1]==0xCEFE1113);auto* data=(std::uint32_t*)out;data[0]=storeTypeId;data[114]=value(base,body,0xFE5DBC0B);storeDestination=out;}
void nativeDestroy(void** out,void*){assert(*(std::uint32_t*)*out==storeTypeId);release(*out);*out=nullptr;}
namespace GameAddress{template<class T>T get_address(std::string_view key){
 if(key=="allocate_game_memory")return reinterpret_cast<T>(&alloc);
 if(key=="StoreObjectsSpecialPower::ParseFields")return reinterpret_cast<T>(&nativeFields);
 assert(key=="BehaviorModuleData::Destroy");return reinterpret_cast<T>(&nativeDestroy);
}}
'''+functions+r'''
int main(){auto b=read("/workspace/work/sdk-wire-audit/add-lure.bin");void* out{};parseAdd(b.data(),wrapper(b,addTypeId),&out,nullptr);auto* data=(YurikoAddLureData*)out;assert(out==storeDestination&&data->storePrefix[0]==addTypeId&&data->storePrefix[114]==4&&data->lureObjectLinkId==7);destroyAdd(&out,nullptr);assert(!out);std::cout<<"actualSDK->runtime AddLure472/inPlaceStore/independentLinks: PASS\n";}
''')

r=Path(str(options.runtime_prefix.resolve())+'-slam')
production=(r/'ext/YurikoImpactExt.cpp').read_text()
functions=production[production.index('    void __cdecl parse('):production.index('    char const* __fastcall name(')]
write_source('audit-impact.cpp',r'''
#include <cassert>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <vector>
#include <array>
#include <fstream>
#include <iterator>
#include <iostream>
#include <string_view>
#include <cmath>
#define __cdecl
#define __fastcall
using Allocate=void*(unsigned);using Free=void(void*);
void* alloc(unsigned n){return std::calloc(1,n);}
void release(void* p){std::free(p);}
std::vector<unsigned char> read(const char* file){std::ifstream f(file,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}};}
std::uint32_t* wrapper(std::vector<unsigned char>& b,std::uint32_t type){for(unsigned i=0;i+12<=b.size();i+=4){auto* p=(std::uint32_t*)(b.data()+i);if(p[0]==2&&p[1]==type&&p[2]<b.size())return p;}std::abort();}
std::uint32_t value(unsigned char* base,std::uint32_t* table,std::uint32_t name){
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==name)return *(std::uint32_t*)(base+r[2]);
 for(auto* r=table;r[0]||r[1];r+=3)if(r[0]==1&&r[2])return value(base,(std::uint32_t*)(base+r[2]),name);
 return 0;
}


#include "ra3defines/YurikoSlam.h"
#include "ra3defines/YurikoImpact.h"
using namespace YurikoImpact;
namespace ra3_types {struct DamageNuggetDefinition {unsigned char bytes[152];};}
unsigned parsed=0,destroyed=0;
std::vector<std::uint32_t*> fixups;
void* behaviorDestination{};
void destroyFields(void*,void*){++destroyed;}
void behaviorFields(unsigned char* base,std::uint32_t* rows,void* output,void*){
 assert(rows[0]==1&&rows[1]==0xD75D93A2);auto* data=(std::uint32_t*)output;
 data[0]=behaviorType;data[1]=value(base,rows,0x8CDC9898);behaviorDestination=output;
 ++parsed;
}
void nuggetFields(unsigned char* base,std::uint32_t* rows,void* output,void*){
 assert(rows[0]==1&&rows[1]==3351432196u);auto* data=(unsigned char*)output;
 auto damage=value(base,rows,0x7C33E4E9);std::memcpy(data+40,&damage,4);
 data[146]=static_cast<unsigned char>(value(base,rows,1874552506u));
 if(value(base,rows,4203588415u)){assert(value(base,rows,4203588415u)==32);fixups.push_back((std::uint32_t*)(data+100));}
 if(value(base,rows,771372620u)){assert(value(base,rows,771372620u)==45);fixups.push_back((std::uint32_t*)(data+104));}
 ++parsed;
}
namespace GameAddress{template<class T>T get_address(std::string_view k){
 if(k=="allocate_game_memory")return reinterpret_cast<T>(&alloc);
 if(k=="deallocate_game_memory")return reinterpret_cast<T>(&release);
 if(k=="DamageNugget::DestroyFields")return reinterpret_cast<T>(&destroyFields);
 if(k=="BehaviorModule::ParseFields")return reinterpret_cast<T>(&behaviorFields);
 assert(k=="DamageNugget::ParseFields");return reinterpret_cast<T>(&nuggetFields);
}}
'''+functions+r'''
int main(){auto b=read("/workspace/work/sdk-wire-audit/collision.bin");void* output{};parse(b.data(),wrapper(b,typeId),&output,nullptr);
auto* data=(Data*)output;assert(data&&data->type==typeId&&data->moduleId&&data->fullDamageSpeed==25&&data->count==2&&data->entries);
auto* definitions=(unsigned char*)(std::uintptr_t)data->entries;float damage;std::memcpy(&damage,definitions+40,4);assert(damage==250&&definitions[146]==0&&definitions[152+146]==1);
assert(output==behaviorDestination&&parsed==3&&fixups.size()==2);
assert(fixups[0]==(std::uint32_t*)(definitions+100)&&fixups[1]==(std::uint32_t*)(definitions+104));
for(auto* field:fixups)*field=0x1234;assert(*(std::uint32_t*)(definitions+100)==0x1234&&*(std::uint32_t*)(definitions+104)==0x1234);
destroyData(&output,nullptr);assert(!output&&destroyed==2);std::cout<<"actualSDK->runtime DamageDynamics20/directDamage152/finalFixups/ownership: PASS\n";}
''')

# Only a platform InitOnce seam is substituted on non-Windows hosts. Production
# parsing/marshalling functions and data structures are compiled as-is.
if options.kind=='gnu':
    (p/'Windows.h').write_text("""#pragma once
#include <mutex>
using BOOL=int;using PVOID=void*;
struct INIT_ONCE{std::mutex mutex;bool ready=false;};using PINIT_ONCE=INIT_ONCE*;
#define INIT_ONCE_STATIC_INIT {}
#define CALLBACK
#define TRUE 1
#define FALSE 0
#define ERROR_NOT_ENOUGH_MEMORY 8
inline void SetLastError(unsigned){}
inline BOOL InitOnceExecuteOnce(PINIT_ONCE once,BOOL(*init)(PINIT_ONCE,PVOID,PVOID*),PVOID param,PVOID* context){std::lock_guard<std::mutex> lock(once->mutex);if(once->ready)return TRUE;if(!init(once,param,context))return FALSE;once->ready=true;return TRUE;}
""",encoding='utf-8')

def build(name,sources,includes,standard):
    output=p/(name+('.exe' if options.kind=='msvc' else ''))
    if options.kind=='msvc':
        command=[options.cxx,'/nologo','/EHsc','/MD','/utf-8','/std:'+standard,*('/I'+str(path)for path in includes),*map(str,sources),'/Fe:'+str(output),'/Fo:'+str(p)+os.sep]
    else:
        command=[options.cxx,'-m32','-O0','-std='+standard,'-I'+str(p),*('-I'+str(path)for path in includes),*map(str,sources),'-o',str(output)]
    subprocess.run(command,check=True)
    return output

names=['Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter','WeaponSlotOrder','WeaponNugget','GameObject']
for feature in ['domination','slam','shield']:
    native=Path(str(options.sdk_prefix.resolve())+'-'+feature)/'recovery/src/Native'
    emitter=build('emit-'+feature,[Path(__file__).with_name('emit.cpp'),*(native/(name+'.cpp')for name in names)],[native],'c++17')
    subprocess.run([*runner,str(emitter),str(p),feature],check=True)

for name,feature in [('lure','domination'),('add-lure','domination'),('slam','slam'),('shield','shield'),('impact','slam')]:
    runtime=Path(str(options.runtime_prefix.resolve())+'-'+feature)
    executable=build('audit-'+name,[p/('audit-'+name+'.cpp')],[runtime],'c++20')
    subprocess.run([*runner,str(executable)],check=True)

def source_identity(prefix, feature):
    root=Path(str(prefix.resolve())+'-'+feature)
    commit=subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True).strip()
    dirty=subprocess.run(['git','-C',str(root),'diff','--quiet','HEAD']).returncode!=0
    return {'commit':commit,'tracked_changes':dirty}

files=['lure.bin','add-lure.bin','add-lift.bin','fling.bin','lift.bin','shield.bin','collision.bin']
receipt={'scope':'Seven actual 32-bit SDK token buffers consumed by extracted runtime parsers. Native parent/field parsers and resource callbacks mocked. No full SDK stream or game execution.',
         'results':'all passed',
         'sources':{kind:{feature:source_identity(prefix,feature)for feature in ['domination','slam','shield']}for kind,prefix in [('sdk',options.sdk_prefix),('runtime',options.runtime_prefix)]},
         'harness_sha256':{file.name:hashlib.sha256(file.read_bytes()).hexdigest()for file in sorted(p.glob('audit-*.cpp'))},
         'emitter_sha256':hashlib.sha256(Path(__file__).with_name('emit.cpp').read_bytes()).hexdigest(),
         'buffers':{name:{'bytes':(p/name).stat().st_size,'sha256':hashlib.sha256((p/name).read_bytes()).hexdigest()}for name in files}}
(p/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print('All seven wire contracts passed; receipt:',p/'receipt.json')
