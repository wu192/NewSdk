#include <array>
#include <chrono>
#include <iostream>
#include <string_view>
#include <vector>
#include "../../src/Native/EnumBitset.cpp"
using namespace Recovered::Native;
static bool space(unsigned char c){return c==' '||(c>=9&&c<=13);}
static void indexed(const std::string& text,const EnumNameValue* names,size_t count,int fallback,unsigned bits,uint32_t* words){
 size_t at=0,end=text.find('\0');if(end==std::string::npos)end=text.size();
 while(at<end){
  while(at<end&&space(text[at]))++at;if(at==end)break;size_t start=at;
  while(at<end&&!space(text[at]))++at;
  std::string_view token(text.data()+start,at-start);bool add=true;
  if(token[0]=='+'||token[0]=='-'){add=token[0]!='-';token.remove_prefix(1);}
  if(token.size()==3&&(token[0]=='a'||token[0]=='A')&&(token[1]=='l'||token[1]=='L')&&(token[2]=='l'||token[2]=='L')){std::fill(words,words+(bits+31)/32,UINT32_MAX);return;}
  auto found=std::lower_bound(names,names+count,token,[](const EnumNameValue& e,std::string_view t){return std::string_view(e.name)<t;});
  int bit=found!=names+count&&token==found->name?found->value:fallback;
  if(bit<0||unsigned(bit)>=bits)continue;
  uint32_t mask=uint32_t(1)<<(bit&31);if(add)words[bit>>5]|=mask;else words[bit>>5]^=mask;
 }
}
static volatile uint32_t sink;
static void probe(const char* label,const EnumNameValue* entries,size_t count,int fallback,unsigned bits){
 for(size_t i=1;i<count;++i)if(std::string(entries[i-1].name)>=entries[i].name)throw std::runtime_error("Unsorted/duplicate names");
 std::vector<std::string> cases={"","ALL -AFLAME","+ALL","-all", "+", "-", "unknown", "aFlAmE", "ALL\tUNKNOWN", std::string("AFLAME\0UNKNOWN",14)};
 for(size_t i=0;i<count;++i){std::string n=entries[i].name;cases.push_back(n);cases.push_back(n+" -"+n+" -"+n);cases.push_back("+"+n+"\tUNKNOWN\n-"+n);}
 for(const auto& text:cases)for(uint32_t seed:{0u,0x55555555u,0xffffffffu}){
  std::array<uint32_t,15>a,b;a.fill(seed);b=a;
  MarshalEnumBitset(text,entries,count,fallback,bits,a.data());indexed(text,entries,count,fallback,bits,b.data());
  if(a!=b)throw std::runtime_error("Differing mask");
 }
 const std::string workload=std::string(entries[count-1].name)+" +"+entries[count/2].name+" -"+entries[count/3].name+" UNKNOWN";
 for(int round=0;round<5;++round)for(int impl=0;impl<2;++impl){
  std::array<uint32_t,15>words{};auto tick=std::chrono::steady_clock::now();
  for(int i=0;i<100000;++i){words.fill(0);if(impl)indexed(workload,entries,count,fallback,bits,words.data());else MarshalEnumBitset(workload,entries,count,fallback,bits,words.data());sink=words[0];}
  double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-tick).count();
  std::cout<<label<<','<<count<<','<<cases.size()*3<<','<<round<<','<<(impl?"view-binary":"original")<<','<<ms<<'\n';
 }
}
int main(){std::cout<<"table,entries,comparisons,round,implementation,milliseconds\n";probe("ModelCondition",modelConditions,sizeof(modelConditions)/sizeof(modelConditions[0]),233,456);probe("ObjectStatus",objectStatus,sizeof(objectStatus)/sizeof(objectStatus[0]),198,223);}
