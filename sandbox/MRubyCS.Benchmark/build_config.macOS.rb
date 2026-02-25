MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  
  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'
  conf.gem './mrbgems/mrubycs-benchmark-helper'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end
