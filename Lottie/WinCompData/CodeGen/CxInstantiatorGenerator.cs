// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using WinCompData.Mgcg;
using WinCompData.Sn;

namespace WinCompData.CodeGen
{
#if !WINDOWS_UWP
    public
#endif
    sealed class CxInstantiatorGenerator : InstantiatorGeneratorBase
    {
        readonly CppStringifier _stringifier;
        readonly string _headerFileName;

        CxInstantiatorGenerator(
            CompositionObject graphRoot, 
            TimeSpan duration, 
            bool setCommentProperties, 
            CppStringifier stringifier,
            string headerFileName)
            : base(graphRoot, duration, setCommentProperties, stringifier)
        {
            _stringifier = stringifier;
            _headerFileName = headerFileName;
        }

        /// <summary>
        /// Returns the Cx code for a factory that will instantiate the given <see cref="Visual"/> as a
        /// Windows.UI.Composition Visual.
        /// </summary>
        public static void CreateFactoryCode(
            string className,
            Visual rootVisual,
            float width,
            float height,
            TimeSpan duration,
            string headerFileName,
            out string cppText,
            out string hText)
        {
            var generator = new CxInstantiatorGenerator(rootVisual, duration, false, new CppStringifier(), headerFileName);

            cppText = generator.GenerateCode(className, rootVisual, width, height);

            hText = GenerateHeaderText(className);
        }

        // Generates the .h file contents.
        static string GenerateHeaderText(string className)
        {
            return
$@"#pragma once
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

#include ""ICompositionSource.h""

namespace Compositions 
{{
ref class {className} sealed : public ICompositionSource
{{
public:
    virtual bool TryCreateInstance(
        Windows::UI::Composition::Compositor^ compositor,
        Windows::UI::Composition::Visual^* rootVisual,
        Windows::Foundation::Numerics::float2* size,
        Windows::Foundation::TimeSpan* duration,
        Platform::Object^* diagnostics);
}};
}}";
        }

        // Called by the base class to write the start of the file (i.e. everything up to the body of the Instantiator class).
        protected override void WriteFileStart(
            CodeBuilder builder,
            CodeGenInfo info)
        {
            builder.WriteLine("#include \"pch.h\"");
            builder.WriteLine($"#include \"{_headerFileName}\"");
            if (info.RequiresWin2d)
            {
                // D2D
                builder.WriteLine("#include \"d2d1.h\"");
                builder.WriteLine("#include <d2d1_1.h>");
                builder.WriteLine("#include <d2d1helper.h>");
                // floatY, floatYxZ
                builder.WriteLine("#include \"WindowsNumerics.h\"");
                // Interop
                builder.WriteLine("#include <Windows.Graphics.Interop.h>");
                // ComPtr
                builder.WriteLine("#include <wrl.h>");
            }
            builder.WriteLine();
            builder.WriteLine("using namespace Windows::Foundation;");
            builder.WriteLine("using namespace Windows::Foundation::Numerics;");
            builder.WriteLine("using namespace Windows::UI;");
            builder.WriteLine("using namespace Windows::UI::Composition;");
            builder.WriteLine("using namespace Windows::Graphics;");
            builder.WriteLine("using namespace Microsoft::WRL;");
            builder.WriteLine();

            // Put the Instantiator class in an anonymous namespace.
            builder.WriteLine("namespace");
            builder.WriteLine("{");

            // Write GeoSource to allow it's use in function definitions
            builder.WriteLine($"{_stringifier.GeoSourceClass}");

            // Typedef to simplify generation
            builder.WriteLine("typedef ComPtr<GeoSource> CanvasGeometry;");

            // Start writing the instantiator.
            builder.WriteLine("class Instantiator final");
            builder.OpenScope();

            // D2D factory field.
            builder.WriteLine("ComPtr<ID2D1Factory> _d2dFactory;");
        }

        // Called by the base class to write the end of the file (i.e. everything after the body of the Instantiator class).
        protected override void WriteFileEnd(
            CodeBuilder builder, 
            CodeGenInfo info)
        {
            // Utility method for D2D geometries
            builder.WriteLine("static IGeometrySource2D^ CanvasGeometryToIGeometrySource2D(CanvasGeometry geo)");
            builder.OpenScope();
            builder.WriteLine("ComPtr<ABI::Windows::Graphics::IGeometrySource2D> interop = geo.Detach();");
            builder.WriteLine("return reinterpret_cast<IGeometrySource2D^>(interop.Get());");
            builder.CloseScope();
            builder.WriteLine();

            // Utility method for fail-fasting on bad HRESULTs from d2d operations
            builder.WriteLine("static void FFHR(HRESULT hr)");
            builder.OpenScope();
            builder.WriteLine("if (hr != S_OK)");
            builder.OpenScope();
            builder.WriteLine("RoFailFastWithErrorContext(hr);");
            builder.CloseScope();
            builder.CloseScope();
            builder.WriteLine();

            // Write the constructor for the instantiator.
            builder.WriteLine("Instantiator(Compositor^ compositor)");
            // Initializer list.
            builder.Indent();
            builder.WriteLine(": _c(compositor)");
            // Instantiate the reusable ExpressionAnimation.
            builder.WriteLine($", {info.ReusableExpressionAnimationFieldName}(compositor->CreateExpressionAnimation())");
            builder.UnIndent();
            builder.OpenScope();
            builder.WriteLine($"{FailFastWrapper("D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, _d2dFactory.GetAddressOf())")};");
            builder.CloseScope();

            // Write the method that instantiates the composition.
            builder.WriteLine();
            builder.UnIndent();
            builder.WriteLine("public:");
            builder.Indent();
            builder.WriteLine("static Visual^ InstantiateComposition(Compositor^ compositor)");
            builder.OpenScope();
            builder.WriteLine($"return Instantiator(compositor).{CallFactoryFor(info.RootVisual)};");
            builder.CloseScope();
            builder.WriteLine();

            // Close the scope for the instantiator class.
            builder.UnIndent();
            builder.WriteLine("};");

            // Close the anonymous namespace.
            builder.WriteLine("} // end namespace");
            builder.WriteLine();


            // Generate the method that creates an instance of the composition.
            builder.WriteLine($"bool Compositions::{info.ClassName}::TryCreateInstance(");
            builder.Indent();
            builder.WriteLine("Compositor^ compositor,");
            builder.WriteLine("Visual^* rootVisual,");
            builder.WriteLine("float2* size,");
            builder.WriteLine("TimeSpan* duration,");
            builder.WriteLine("Object^* diagnostics)");
            builder.UnIndent();
            builder.OpenScope();
            builder.WriteLine("*rootVisual = Instantiator::InstantiateComposition(compositor);");
            builder.WriteLine($"*size = {Vector2(info.CompositionDeclaredSize)};");
            builder.WriteLine($"duration->Duration = {_stringifier.TimeSpan(info.CompositionDuration)};");
            builder.WriteLine("diagnostics = nullptr;");
            builder.WriteLine("return true;");
            builder.CloseScope();

        }

        protected override void WriteCanvasGeometryCombinationFactory(CodeBuilder builder, CanvasGeometry.Combination obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ID2D1Geometry **geoA = new ID2D1Geometry*, **geoB = new ID2D1Geometry*;");
            builder.WriteLine($"{CallFactoryFor(obj.A)}->GetGeometry(geoA);");
            builder.WriteLine($"{CallFactoryFor(obj.B)}->GetGeometry(geoB);");
            builder.WriteLine("ComPtr<ID2D1PathGeometry> path;");
            builder.WriteLine($"{FailFastWrapper("_d2dFactory->CreatePathGeometry(&path)")};");
            builder.WriteLine("ComPtr<ID2D1GeometrySink> sink;");
            builder.WriteLine($"{FailFastWrapper("path->Open(&sink)")};");
            builder.WriteLine($"FFHR((*geoA)->CombineWithGeometry(");
            builder.Indent();
            builder.WriteLine($"*geoB,");
            builder.WriteLine($"{_stringifier.CanvasGeometryCombine(obj.CombineMode)},");
            builder.WriteLine($"{_stringifier.Matrix3x2(obj.Matrix)},");
            builder.WriteLine($"sink.Get()));");
            builder.UnIndent();
            builder.WriteLine($"{FailFastWrapper("sink->Close()")};");
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(path.Get());");
        }

        protected override void WriteCanvasGeometryEllipseFactory(CodeBuilder builder, CanvasGeometry.Ellipse obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ComPtr<ID2D1EllipseGeometry> ellipse;");
            builder.WriteLine("FFHR(_d2dFactory->CreateEllipseGeometry(");
            builder.Indent();
            builder.WriteLine($"D2D1::Ellipse({{{Float(obj.X)},{Float(obj.Y)}}}, {Float(obj.RadiusX)}, {Float(obj.RadiusY)}),");
            builder.WriteLine("&ellipse));");
            builder.UnIndent();
            builder.CloseScope();
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(ellipse.Get());");
        }

        protected override void WriteCanvasGeometryPathFactory(CodeBuilder builder, CanvasGeometry.Path obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");

            // D2D Setup
            builder.WriteLine("ComPtr<ID2D1PathGeometry> path;");
            builder.WriteLine($"{FailFastWrapper("_d2dFactory->CreatePathGeometry(&path)")};");
            builder.WriteLine("ComPtr<ID2D1GeometrySink> sink;");
            builder.WriteLine($"{FailFastWrapper("path->Open(&sink)")};");

            if (obj.FilledRegionDetermination != CanvasFilledRegionDetermination.Alternate)
            {
                builder.WriteLine($"sink->SetFillMode({FilledRegionDetermination(obj.FilledRegionDetermination)});");
            }

            foreach (var command in obj.Commands)
            {
                switch (command.Type)
                {
                    case CanvasPathBuilder.CommandType.BeginFigure:
                        // Assume D2D1_FIGURE_BEGIN_FILLED
                        builder.WriteLine($"sink->BeginFigure({Vector2(((CanvasPathBuilder.Command.BeginFigure)command).StartPoint)}, D2D1_FIGURE_BEGIN_FILLED);");
                        break;
                    case CanvasPathBuilder.CommandType.EndFigure:
                        builder.WriteLine($"sink->EndFigure({CanvasFigureLoop(((CanvasPathBuilder.Command.EndFigure)command).FigureLoop)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddLine:
                        builder.WriteLine($"sink->AddLine({Vector2(((CanvasPathBuilder.Command.AddLine)command).EndPoint)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddCubicBezier:
                        var cb = (CanvasPathBuilder.Command.AddCubicBezier)command;
                        builder.WriteLine($"sink->AddBezier({{ {Vector2(cb.ControlPoint1)}, {Vector2(cb.ControlPoint2)}, {Vector2(cb.EndPoint)} }});");
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
            builder.WriteLine($"{FailFastWrapper("sink->Close()")};");
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(path.Get());");
        }

        protected override void WriteCanvasGeometryRoundedRectangleFactory(CodeBuilder builder, CanvasGeometry.RoundedRectangle obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ComPtr<ID2D1RoundedRectangleGeometry> rect;");
            builder.WriteLine("FFHR(_d2dFactory->CreateRoundedRectangleGeometry(");
            builder.Indent();
            builder.WriteLine($"D2D1::RoundedRect({{{Float(obj.X)},{Float(obj.Y)}}}, {Float(obj.RadiusX)}, {Float(obj.RadiusY)}),");
            builder.WriteLine("&rect));");
            builder.UnIndent();
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(rect.Get());");
        }

        string CanvasFigureLoop(CanvasFigureLoop value) => _stringifier.CanvasFigureLoop(value);
        static string FieldAssignment(string fieldName) => (fieldName != null ? $"{fieldName} = " : "");
        string FilledRegionDetermination(CanvasFilledRegionDetermination value) => _stringifier.FilledRegionDetermination(value);
        string Float(float value) => _stringifier.Float(value);
        string FailFastWrapper(string value) => _stringifier.FailFastWrapper(value);
        string Vector2(Vector2 value) => _stringifier.Vector2(value);
    }
}
