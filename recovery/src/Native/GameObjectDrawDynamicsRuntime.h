// Dynamics-specific traversal. The caller applies the full ScriptedModel parent afterwards.
void MarshalDrawDynamicsSchema(const GameObjectChild& input,unsigned char* output,const DrawDynamicsSchema& schema,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,schema.fields,data);
    for(unsigned ci=0;ci<schema.count;++ci) {
        const auto& c=schema.children[ci];
        if(c.mode==1) {
            auto list=reinterpret_cast<NativeList*>(output+c.offset);
            list->count=0;for(const auto& node:input.children)if(node.name==c.name)++list->count;
            if(list->count) {auto rows=static_cast<unsigned char*>(data.Push(&list->data,c.size,list->count));unsigned i=0;for(const auto& node:input.children)if(node.name==c.name)MarshalDrawDynamicsSchema(node,rows+(i++)*c.size,*c.nested,data);data.Pop();}
        } else {
            // INode::GetChild selects the first matching element.
            for(const auto& node:input.children)if(node.name==c.name) {
                auto row=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+c.offset),c.size,1));
                if(c.nested)MarshalDrawDynamicsSchema(node,row,*c.nested,data);
                else {const char* coordinates[] = {"x","y","z","w"};for(unsigned i=0;i<c.size/4;++i){auto it=node.attributes.find(coordinates[i]);float v=ParseFloat(it==node.attributes.end()?"0.0":it->second);std::memcpy(row+4*i,&v,4);}}
                data.Pop();break;
            }
        }
    }
    if(schema.parent)MarshalDrawDynamicsSchema(input,output,*schema.parent,data);
}
void EndianDrawDynamicsSchema(unsigned char* output,const DrawDynamicsSchema& schema) {
    EndianModuleFieldSet(output,schema.fields);
    for(unsigned ci=0;ci<schema.count;++ci) {
        const auto& c=schema.children[ci];
        if(c.mode==1) {auto list=reinterpret_cast<NativeList*>(output+c.offset);for(U32 i=0;i<list->count;++i)EndianDrawDynamicsSchema(static_cast<unsigned char*>(list->data)+i*c.size,*c.nested);Swap(&list->count);}
        else {auto row=*reinterpret_cast<unsigned char**>(output+c.offset);if(row){if(c.nested)EndianDrawDynamicsSchema(row,*c.nested);else for(unsigned i=0;i<c.size;i+=4)Swap(row+i);}}
    }
    if(schema.parent)EndianDrawDynamicsSchema(output,*schema.parent);
}
void MarshalDrawDynamics(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {MarshalDrawDynamicsSchema(input,output,drawDynamicsSchemaW3DDynamicsDrawModuleData,data);}
void EndianDrawDynamics(unsigned char* output) {EndianDrawDynamicsSchema(output,drawDynamicsSchemaW3DDynamicsDrawModuleData);}
bool DrawDynamicsSchemaAttribute(const DrawDynamicsSchema& schema,const std::string& name) {
    for(unsigned i=0;i<schema.fields.count;++i)if(name==schema.fields.fields[i].name)return true;
    return schema.parent&&DrawDynamicsSchemaAttribute(*schema.parent,name);
}

const DrawDynamicsChild* DrawDynamicsFindChild(const DrawDynamicsSchema& schema,const std::string& name) {
    for(unsigned i=0;i<schema.count;++i)if(name==schema.children[i].name)return schema.children+i;
    return schema.parent?DrawDynamicsFindChild(*schema.parent,name):nullptr;
}
const DrawDynamicsChild* DrawDynamicsPath(const std::string& path) {
    const DrawDynamicsSchema* schema=&drawDynamicsSchemaW3DDynamicsDrawModuleData;const DrawDynamicsChild* c=nullptr;std::size_t begin=0;
    while(begin<path.size()){auto slash=path.find('/',begin);std::string part=path.substr(begin,slash==std::string::npos?slash:slash-begin);c=DrawDynamicsFindChild(*schema,part);if(!c)return nullptr;if(slash==std::string::npos)return c;if(!c->nested)return nullptr;schema=c->nested;begin=slash+1;}
    return c;
}
bool DrawDynamicsAttribute(const std::string& path,const std::string& name) {auto c=DrawDynamicsPath(path);if(!c)return false;if(c->nested)return DrawDynamicsSchemaAttribute(*c->nested,name);return name=="x"||name=="y"||name=="z"||(c->size==16&&name=="w");}
unsigned DrawDynamicsNodeKind(const std::string& path) {auto c=DrawDynamicsPath(path);return c?(c->mode==1?2:1):0;}
