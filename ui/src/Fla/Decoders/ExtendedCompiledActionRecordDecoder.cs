/*
 * Copyright (C) 2026 JPEXS.
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston,
 * MA 02110-1301  USA
 */
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace BinaryAssetBuilder.UiCompiler.Fla
{



internal class ExtendedCompiledActionRecordDecoder {

    private ExtendedCompiledActionRecordDecoder() {
    }

    internal static void read(MfcArchive a, FlaRecord r, long rawType, long listRevision) {
        if (rawType > 85 || rawType == 35 || rawType < 0) {
            throw new UnsupportedFlaRecordException("Compiled action command type " + rawType);
        }
        int type = (int) rawType;
        bool present = listRevision != 0 || (type != 46 && type != 82);
        r.set("bodyPresent", present);
        if (!present) {
            return;
        }
        int maximum = type == 37 ? 4
                : type == 39 || type == 41 || type == 44 || type == 47 || type == 59
                || type == 78 || type == 80 || type == 83 ? 3 : 2;
        long revision = CompiledActionRecordDecoder.revision(a, r, maximum);
        Fields fields = new Fields(a, r, type, revision);
        int metadataRevision = type == 37 ? 4
                : type == 39 || type == 41 || type == 44 || type == 47 || type == 59 || type == 80 ? 3 : 2;
        switch (type) {
            case 6:
            case 49:
                fields.parameter();
                fields.parameter();
                break;
            case 7:
                fields.word();
                fields.parameter();
                fields.word();
                fields.parameter();
                fields.word();
                break;
            case 8:
            case 22:
            case 27:
            case 30:
            case 36:
                fields.parameter();
                break;
            case 10:
                fields.@string();
                fields.@string();
                fields.word();
                break;
            case 15:
            case 17:
            case 33:
            case 40:
            case 41:
            case 43:
            case 50:
            case 53:
            case 57:
            case 65:
            case 69:
            case 75:
            case 84:
                fields.@string();
                break;
            case 20:
                fields.@string();
                if (revision >= 2) {
                    r.set("emptyBody", a.readUI8());
                }
                break;
            case 23:
                fields.word();
                fields.parameter();
                fields.parameter();
                break;
            case 24:
                fields.parameter();
                fields.parameter();
                break;
            case 25:
                fields.word();
                fields.parameter();
                fields.parameter();
                fields.@string();
                break;
            case 26:
                fields.parameter();
                for (int i = 0; i < 4; i++) {
                    fields.@string();
                }
                fields.word();
                fields.word();
                fields.word();
                break;
            case 28:
                fields.@string();
                break;
            case 31:
                fields.parameter();
                fields.word();
                break;
            case 32:
                fields.parameter();
                fields.word();
                fields.parameter();
                fields.word();
                break;
            case 34:
                fields.word();
                fields.parameter();
                fields.word();
                fields.word();
                fields.word();
                break;
            case 37:
                fields.@string();
                fields.@string();
                if (revision >= 1) {
                    fields.word();
                }
                if (revision >= 3) {
                    r.set("declarationOnly", a.readUI8());
                }
                break;
            case 39:
                fields.@string();
                if (revision >= 1) {
                    fields.word();
                }
                break;
            case 44:
                fields.@string();
                if (revision >= 1) {
                    fields.@string();
                    fields.@string();
                }
                break;
            case 46:
            case 82:
                if (revision >= 2) {
                    CompiledActionRecordDecoder.metadata(a, r);
                    fields.parameter();
                }
                return;
            case 47:
            case 73:
            case 80:
                fields.@string();
                fields.@string();
                break;
            case 55:
            case 56:
                fields.@string();
                fields.word();
                break;
            case 59:
                fields.@string();
                fields.@string();
                fields.@string();
                if (revision >= 1) {
                    fields.word();
                    fields.word();
                }
                break;
            case 60:
            case 62:
            case 79:
                fields.@string();
                fields.@string();
                fields.@string();
                break;
            case 63:
            case 67:
            case 77:
                break;
            case 71:
                fields.@string();
                fields.@string();
                fields.@string();
                fields.word();
                if (revision >= 1) {
                    fields.word();
                }
                break;
            case 76:
            case 78:
            case 83:
                if (revision >= 2) {
                    fields.@string();
                    fields.word();
                    fields.word();
                }
                if (revision >= 3) {
                    fields.@string();
                    fields.@string();
                }
                return;
            default:
                throw new UnsupportedFlaRecordException("Compiled action command type " + type);
        }
        if (revision >= metadataRevision) {
            if (type == 24) {
                r.set("sourcePrefix", a.readString());
                r.set("sourceText", a.readString());
                r.set("sourceSuffix", a.readString());
                r.set("value40", a.readUI32());
                r.set("valueQuoteCharacter", a.readUI16());
                r.set("suppressBlockDelimiter", a.readUI32());
            } else {
                CompiledActionRecordDecoder.metadata(a, r);
            }
            if (type == 28) {
                fields.word();
            }
        }
    }

    
    private class Fields {

        private MfcArchive archive;
        private FlaRecord record;
        private int type;
        private long revision;
        private string[] stringNames, wordNames;
        private int strings;
        private int words;
        private int parameters;

        internal Fields(MfcArchive archive, FlaRecord record, int type, long revision) {
            this.archive = archive;
            this.record = record;
            this.type = type;
            this.revision = revision;
            this.stringNames = operandNames(true);
            this.wordNames = operandNames(false);
        }

        internal void @string() {
            record.set(operandName(true, ++strings), archive.readString());
        }

        internal void word() {
            record.set(operandName(false, ++words), archive.readUI32());
        }

        private string[] operandNames(bool isString) {
            string[] names = null;
            if (isString) {
                switch (type) {
                    case 10:
                        names = new string[]{"scene", "frame"};
                        break;
                    case 15:
                        names = new string[]{"condition"};
                        break;
                    case 17:
                        names = new string[]{"condition"};
                        break;
                    case 20:
                        names = new string[]{"condition"};
                        break;
                    case 25:
                        names = new string[]{"depth"};
                        break;
                    case 26:
                        names = new string[]{"left", "top", "right", "bottom"};
                        break;
                    case 28:
                        names = new string[]{"comment"};
                        break;
                    case 33:
                        names = new string[]{"includePath"};
                        break;
                    case 37:
                        names = new string[]{"functionSignature", "parameters"};
                        break;
                    case 39:
                        names = new string[]{"declaration"};
                        break;
                    case 40:
                        names = new string[]{"returnValue"};
                        break;
                    case 41:
                        names = new string[]{"expression"};
                        break;
                    case 43:
                        names = new string[]{"condition"};
                        break;
                    case 44:
                        names = revision == 0 ? new string[]{"condition"} : new string[]{"initializer", "condition", "increment"};
                        break;
                    case 47:
                        names = new string[]{"iterator", "collection"};
                        break;
                    case 50:
                        names = new string[]{"object"};
                        break;
                    case 53:
                        names = new string[]{"expression"};
                        break;
                    case 55:
                        names = new string[]{"caseExpression"};
                        break;
                    case 56:
                        names = new string[]{"caseExpression"};
                        break;
                    case 57:
                        names = new string[]{"initClipIndex"};
                        break;
                    case 59:
                        names = new string[]{"invocationSignature", "object", "arguments"};
                        break;
                    case 60:
                        names = new string[]{"object", "method", "parameters"};
                        break;
                    case 62:
                        names = new string[]{"object", "method", "arguments"};
                        break;
                    case 65:
                        names = new string[]{"exception"};
                        break;
                    case 69:
                        names = new string[]{"expression"};
                        break;
                    case 71:
                        names = new string[]{"name", "superClass", "interfaces"};
                        break;
                    case 73:
                        names = new string[]{"name", "interfaces"};
                        break;
                    case 75:
                        names = new string[]{"importName"};
                        break;
                    case 76:
                        names = new string[]{"packageName"};
                        break;
                    case 78:
                        names = new string[]{"namespaceName", "namespaceValue", "namespaceQualifier"};
                        break;
                    case 79:
                        names = new string[]{"object", "method", "arguments"};
                        break;
                    case 80:
                        names = new string[]{"iterator", "collection"};
                        break;
                    case 83:
                        names = new string[]{"namespaceName", "namespaceValue", "namespaceQualifier"};
                        break;
                    case 84:
                        names = new string[]{"configuration"};
                        break;
                    default:
                        break;
                }
            } else {
                switch (type) {
                    case 7:
                        names = new string[]{"discardedLegacyDword", "requestMethod", "targetMode"};
                        break;
                    case 10:
                        names = new string[]{"frameMode"};
                        break;
                    case 23:
                        names = new string[]{"propertyIndex"};
                        break;
                    case 25:
                        names = new string[]{"discardedLegacyDword"};
                        break;
                    case 76:
                    case 78:
                    case 83:
                        names = new string[]{"value40", "suppressBlockDelimiter"};
                        break;
                    case 26:
                        names = new string[]{"lockCenter", "constrain"};
                        break;
                    case 28:
                        names = new string[]{"commentStyle"};
                        break;
                    case 31:
                        names = new string[]{"targetMode"};
                        break;
                    case 32:
                        names = new string[]{"requestMethod", "targetMode"};
                        break;
                    case 34:
                        names = new string[]{"discardedLegacyDword", "targetMode", "boundsMode", "printAsBitmap"};
                        break;
                    case 37:
                        names = new string[]{"functionFlags"};
                        break;
                    case 39:
                        names = new string[]{"variableFlags"};
                        break;
                    case 55:
                        names = new string[]{"isDefaultCase"};
                        break;
                    case 56:
                        names = new string[]{"isDefaultCase"};
                        break;
                    case 59:
                        names = new string[]{"objectExpressionFlags"};
                        break;
                    case 71:
                        names = new string[]{"dynamic", "intrinsic"};
                        break;
                    default:
                        break;
                }
            }
            return names;
        }

        private string operandName(bool isString, int index) {
            string[] names = isString ? stringNames : wordNames;
            return names != null && index <= names.Length && names[index - 1] != null
                    ? names[index - 1] : (isString ? "string" : "value") + index;
        }

        internal void parameter() {
            archive.readInline(record, "parameter" + ++parameters, (@in, parameter) => {
                parameter.set("text", @in.readString());
                parameter.set("flags", @in.readUI32());
            });
        }
    }
}

}
