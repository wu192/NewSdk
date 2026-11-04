#include <chrono>
#include <iostream>
#include "../../src/Native/Relocation.cpp"
using namespace Recovered::Relo;
struct Range {uintptr_t start,end;U32 base;};
static bool indexed(const std::vector<Range>& ranges,const void* member,U32& offset){
 auto address=reinterpret_cast<uintptr_t>(member);
 auto it=std::upper_bound(ranges.begin(),ranges.end(),address,[](uintptr_t a,const Range& b){return a<b.start;});
 if(it==ranges.begin())return false;--it;if(address>=it->end)return false;offset=it->base+U32(address-it->start);return true;
}
static volatile U32 sink;
int main(){std::cout<<"blocks,queries,round,implementation,milliseconds\n";
 for(U32 blocks:{8,32,128,512,2048}){
  Tracker owner;std::vector<Range> ranges;std::vector<const void*> queries;
  for(U32 i=0;i<blocks;++i){void* p=nullptr;owner.Push(&p,64,1);owner.Pop();auto a=reinterpret_cast<uintptr_t>(p);ranges.push_back({a,a+64,i*64});for(U32 j=0;j<24;++j)queries.push_back(static_cast<char*>(p)+(j*7)%64);}
  auto build=std::chrono::steady_clock::now();auto index=ranges;std::stable_sort(index.begin(),index.end(),[](const Range& a,const Range& b){return a.start<b.start;});
  double buildMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-build).count();
  std::cout<<blocks<<','<<queries.size()<<",-1,build-index,"<<buildMs<<'\n';
  for(auto q:queries){U32 a=0,b=0;if(!owner.GetMemberOffset(q,a)||!indexed(index,q,b)||a!=b)throw std::runtime_error("Different offset");}
  U32 a=123,b=123;int outside=0;if(owner.GetMemberOffset(&outside,a)||indexed(index,&outside,b)||a!=123||b!=123)throw std::runtime_error("Failure did not preserve offset");
  for(int round=0;round<5;++round)for(int impl=0;impl<2;++impl){auto tick=std::chrono::steady_clock::now();
   for(int repeat=0;repeat<8;++repeat)for(auto q:queries){U32 at=0;if(impl)indexed(index,q,at);else owner.GetMemberOffset(q,at);sink=at;}
   double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-tick).count();
   std::cout<<blocks<<','<<queries.size()*8<<','<<round<<','<<(impl?"address-index":"original")<<','<<ms<<'\n';
  }
 }
}
