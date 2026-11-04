#include <chrono>
#include <cstdlib>
#include <iostream>
#include <map>
#include <new>
#include <string>
static bool countAllocations=false;
static size_t allocationCount=0;
void* operator new(std::size_t n){if(countAllocations)++allocationCount;if(void* p=std::malloc(n?n:1))return p;throw std::bad_alloc();}
void operator delete(void* p)noexcept{std::free(p);}
void operator delete(void* p,std::size_t)noexcept{std::free(p);}
static const char* keys[]={
#include "attribute-keys.inc"
};
static volatile size_t sink;
template<class Map>void run(const Map& map,size_t repeats){size_t found=0;for(size_t i=0;i<repeats;++i)for(auto key:keys)found+=map.find(key)!=map.end();sink=found;}
int main(){std::cout<<"fixture,round,implementation,passes,milliseconds,lookupAllocationsPerPass\n";
 for(int size:{0,20}){
  std::map<std::string,std::string> before;std::map<std::string,std::string,std::less<>> after;
  for(int i=0;i<size;++i){before.emplace(keys[i],"value");after.emplace(keys[i],"value");}
  size_t counts[2];for(int impl=0;impl<2;++impl){allocationCount=0;countAllocations=true;if(impl)run(after,1);else run(before,1);countAllocations=false;counts[impl]=allocationCount;}
  for(int round=0;round<5;++round)for(int order=0;order<2;++order){int impl=order^(round&1);auto tick=std::chrono::steady_clock::now();if(impl)run(after,20000);else run(before,20000);double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-tick).count();std::cout<<size<<','<<round<<','<<(impl?"transparent":"original")<<",20000,"<<ms<<','<<counts[impl]<<'\n';}
 }
}
