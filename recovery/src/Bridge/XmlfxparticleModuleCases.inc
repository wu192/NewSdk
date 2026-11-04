// Original Module Marshal 22344-22701 and 23433-23866; polymorphic sizes 61846-61885.
        case 2933826200u: { // FXParticleUpdateDefault
            input.size=104;

            array<String^>^ names={"SizeRate","SizeRateDamping","AngleZ","AngularRateZ","AngularDamping","AngleXY","AngularRateXY","AngularDampingXY"};

            Field(node,"Rotation","ROTATION_OFF",4,Native::ParticleEnum,"FXParticleSystem_RotationType",values);
            for(int i=0;i<8;++i) {
                if(i==1 || i==7) InitializedModuleRandom(node,names[i],8+12*i,input);
                else ModuleRandom(node,names[i],8+12*i,input);
            }
            break;
        }
        case 234581578u: { // FXParticleUpdateRenderObject
            input.size=200;

            Field(node,"Rotation","ROTATION_OFF",4,Native::ParticleEnum,"FXParticleSystem_RotationType",values);
            array<String^>^ names={"AngleX","AngularRateX","AngleY","AngularRateY","AngleZ","AngularRateZ","AngularDamping"};
            for(int i=0;i<7;++i) ModuleRandom(node,names[i],8+12*i,input);
            ModuleRandCoord(node,"StartSize",92,input);
            ModuleRandCoord(node,"SizeRate",128,input);
            ModuleRandCoord(node,"SizeDamping",164,input);
            break;
        }
        case 2248017887u: { // FXParticleDrawGpu
            input.size=60;

            Field(node,"Shader","GPUParticle.fx",4,Native::ParticleString,nullptr,values);
            Field(node,"FramesPerRow","1",12,Native::ParticleSigned,nullptr,values);
            Field(node,"TotalFrames","1",16,Native::ParticleSigned,nullptr,values);
            Field(node,"SingleRow","0",20,Native::ParticleSigned,nullptr,values);
            Field(node,"RandTex","0",24,Native::ParticleSigned,nullptr,values);
            Field(node,"DetailTexture",nullptr,28,Native::ParticleReference,nullptr,values);
            Field(node,"SpeedMultiplier","1.0",32,Native::ParticleFloat,nullptr,values);
            Field(node,"GeometryType","SIMPLE_QUAD",36,Native::ParticleEnum,"FXParticleSystem_GeometryType",values);
            Field(node,"MiscValue1","0.0",40,Native::ParticleFloat,nullptr,values);
            Field(node,"MiscValue2","0.0",44,Native::ParticleFloat,nullptr,values);
            Field(node,"MiscValue3","0.0",48,Native::ParticleFloat,nullptr,values);
            Field(node,"MiscValue4","0.0",52,Native::ParticleFloat,nullptr,values);
            Field(node,"SortParticles","false",56,Native::ParticleBoolean,nullptr,values);
            Field(node,"EnableSoftParticles","true",57,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 3036920908u: { // FXParticleDrawLightning
            input.size=36;

            // Repeated list entries are allowed, vector singletons remain unique.

            Vector(node,"StartPoint",4,false,values); Vector(node,"EndPoint",16,false,values);
            array<String^>^ names={"StartAmplitude","EndAmplitudeChange","StartFrequency","EndFrequencyChange","StartPhase","EndPhaseChange"};
            for each(XmlNode^ child in node->ChildNodes) if(child->NodeType==XmlNodeType::Element && child->LocalName=="RandomSet") {

                Native::XmlfxparticleLightningSetInput item;
                for(int i=0;i<6;++i) Random(Child(child,names[i]),item.random[i]);
                input.lightningSets.push_back(item);
            }
            break;
        }
        case 3408220251u: { // FXParticleDrawRenderObject
            input.size=20;

            Field(node,"SinkRate","0.0",4,Native::ParticleFloat,nullptr,values);
            Field(node,"MultiRenderObjects","false",16,Native::ParticleBoolean,nullptr,values);
            Field(node,"SinkOnTerrainCollision","false",17,Native::ParticleBoolean,nullptr,values);
            for each(XmlNode^ child in node->ChildNodes) if(child->NodeType==XmlNodeType::Element) {

                Native::XmlfxparticleObjectSetInput item;
                item.hasRenderGroup=child->Attributes["RenderGroup"]!=nullptr;
                item.renderGroup=Attr(child,"RenderGroup","");
                item.numObjects=Attr(child,"NumObjects","0");
                item.percent=Attr(child,"Percent","0.0");
                input.objectSets.push_back(item);
            }
            break;
        }
