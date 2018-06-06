// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using WinCompData.Sn;
using WinCompData.Wui;

namespace WinCompData.CodeGen
{
    /// <summary>
    /// Stringifiers for C++ syntax.
    /// </summary>
    sealed class CppStringifier : InstantiatorGeneratorBase.StringifierBase
    {
        public override string CanvasFigureLoop(Mgcg.CanvasFigureLoop value)
        {
            switch (value)
            {
                case Mgcg.CanvasFigureLoop.Open:
                    return "D2D1_FIGURE_END_OPEN";
                case Mgcg.CanvasFigureLoop.Closed:
                    return "D2D1_FIGURE_END_CLOSED";
                default:
                    throw new InvalidOperationException();
            }
        }

        public override string CanvasGeometryCombine(Mgcg.CanvasGeometryCombine value)
        {
            switch (value)
            {
                case Mgcg.CanvasGeometryCombine.Union:
                    return "D2D1_COMBINE_MODE_UNION";
                case Mgcg.CanvasGeometryCombine.Exclude:
                    return "D2D1_COMBINE_MODE_EXCLUDE";
                case Mgcg.CanvasGeometryCombine.Intersect:
                    return "D2D1_COMBINE_MODE_INTERSECT";
                case Mgcg.CanvasGeometryCombine.Xor:
                    return "D2D1_COMBINE_MODE_XOR";
                default:
                    throw new InvalidOperationException();
            }
        }

        public override string Color(Color value) => $"ColorHelper::FromArgb({Hex(value.A)}, {Hex(value.R)}, {Hex(value.G)}, {Hex(value.B)})";

        public override string Deref => "->";

        public override string FilledRegionDetermination(Mgcg.CanvasFilledRegionDetermination value)
        {
            switch (value)
            {
                case Mgcg.CanvasFilledRegionDetermination.Alternate:
                    return "D2D1_FILL_MODE_ALTERNATE";
                case Mgcg.CanvasFilledRegionDetermination.Winding:
                    return "D2D1_FILL_MODE_WINDING";
                default:
                    throw new InvalidOperationException();
            }
        }

        public override string Int64(long value) => $"{value}L";

        public override string Int64TypeName => "int64_t";

        public override string ScopeResolve => "::";

        public override string New => "ref new";

        public override string Null => "nullptr";

        public override string Matrix3x2(Matrix3x2 value)
        {
            return $"{{{Float(value.M11)}, {Float(value.M12)}, {Float(value.M21)}, {Float(value.M22)}, {Float(value.M31)}, {Float(value.M32)}}}";
        }

        public override string Readonly => "";

        public override string ReferenceTypeName(string value) =>
            value == "CanvasGeometry"
                // C++ uses a typdef for CanvasGeometry that is ComPtr<GeoSource>, thus no hat pointer
                ? "CanvasGeometry"
                : $"{value}^";

        public override string TimeSpan(TimeSpan value) => TimeSpan(Int64(value.Ticks));
        public override string TimeSpan(string ticks) => $"{{ {ticks} }}";

        public override string Var => "auto";

        public override string Vector2(Vector2 value) => $"{{ {Float(value.X)}, {Float(value.Y)} }}";

        public override string Vector3(Vector3 value) => $"{{ {Float(value.X)}, {Float(value.Y)}, {Float(value.Z)} }}";

        public override string IListAdd => "Append";

        public override string FactoryCall(string value) => $"CanvasGeometryToIGeometrySource2D({value})";

        public string FailFastWrapper(string value) => $"FFHR({value})";

        public string GeoSourceClass =>
              @"class GeoSource :
            public ABI::Windows::Graphics::IGeometrySource2D,
            public ABI::Windows::Graphics::IGeometrySource2DInterop
        {
        public:
            GeoSource(
                ID2D1Geometry* pGeometry)
                : m_cRef(0)
                , m_cpGeometry(pGeometry)
            {
            }

        protected:
            ~GeoSource() = default;

        public:
            // IUnknown
            IFACEMETHODIMP QueryInterface(
                REFIID iid,
                void ** ppvObject) override
            {
                if (iid == __uuidof(ABI::Windows::Graphics::IGeometrySource2DInterop))
                {
                    AddRef();
                    *ppvObject = (ABI::Windows::Graphics::IGeometrySource2DInterop*) this;
                    return S_OK;
                }

                return E_NOINTERFACE;
            }

            IFACEMETHODIMP_(ULONG) AddRef() override
            {
                return InterlockedIncrement(&m_cRef);
            }

            IFACEMETHODIMP_(ULONG) Release() override
            {
                ULONG cRef = InterlockedDecrement(&m_cRef);
                if (0 == cRef)
                {
                    delete this;
                }
                return cRef;
            }

            // IInspectable
            IFACEMETHODIMP GetIids(ULONG*, IID**) override
            {
                return E_NOTIMPL;
            }

            IFACEMETHODIMP GetRuntimeClassName(HSTRING*) override
            {
                return E_NOTIMPL;
            }

            IFACEMETHODIMP GetTrustLevel(TrustLevel*) override
            {
                return E_NOTIMPL;
            }

            // Windows::Graphics::IGeometrySource2DInterop
            IFACEMETHODIMP GetGeometry(ID2D1Geometry** value) override
            {
                *value = m_cpGeometry.Get();
                (*value)->AddRef();
                return S_OK;
            }

            IFACEMETHODIMP TryGetGeometryUsingFactory(ID2D1Factory*, ID2D1Geometry**) override
            {
                return E_NOTIMPL;
            }

        private:
            ULONG m_cRef;
            Microsoft::WRL::ComPtr<ID2D1Geometry> m_cpGeometry;
        };
";

    }
}
